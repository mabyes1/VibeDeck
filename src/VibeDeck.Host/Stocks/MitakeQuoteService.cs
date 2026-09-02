using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using VibeDeck.Host.Dashboard;

namespace VibeDeck.Host.Stocks
{
    public sealed class MitakeQuoteService : IHostedService, IDisposable
    {
        private const string ServiceName = "Mitake";
        private const string TopicName = "Quote";
        // Name comes from broker portfolio metadata, so DDE only subscribes
        // fields that actually change during the session.
        private static readonly string[] QuoteFields = { "Deal", "DiffPercent", "BidPrice", "TotalVol" };

        private const uint AppCmdClientOnly = 0x00000010;
        private const int CpWinAnsi = 1004;
        private const uint CfText = 1;
        private const int XClassBool = 0x1000;
        private const int XClassFlags = 0x4000;
        private const int XClassNotification = 0x8000;
        private const int XtypAdvData = 0x0010 | XClassFlags;
        private const int XtypAdvStart = 0x0030 | XClassBool;
        private const int XtypAdvStop = 0x0040 | XClassNotification;
        private const int XtypDisconnect = 0x00C0 | XClassNotification | 0x0002;
        private const int DdeFAck = 0x8000;
        private const int WarmupSubscriptionTimeoutMs = 1500;
        private const int SubscriptionTimeoutMs = 350;
        private const int StopTimeoutMs = 100;
        private const int SubscriptionBatchSize = 8;
        private const int DebugEventLimit = 160;
        private const uint PmRemove = 0x0001;

        private readonly DashboardEventHub eventHub;
        private readonly object stateGate = new object();
        private readonly Dictionary<string, MutableQuote> quotes = new Dictionary<string, MutableQuote>(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly string portfoliosDirectory;
        private readonly Queue<StockDebugEvent> debugEvents = new Queue<StockDebugEvent>();
        private readonly Stopwatch lifetime = Stopwatch.StartNew();

        private Thread thread;
        private DdeCallback callback;
        private uint instanceId;
        private bool connected;
        private bool ddeDisconnected;
        private bool dirty;
        private long revision;
        private DateTimeOffset? updatedAt;
        private DateTimeOffset nextPublishAt = DateTimeOffset.MinValue;
        private int readySymbolCount;
        private bool firstAdvDataLogged;
        private int disposed;

        public MitakeQuoteService(DashboardEventHub eventHub)
        {
            this.eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            portfoliosDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StockDeck",
                "portfolios");
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows()) return Task.CompletedTask;

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "VibeDeck Mitake DDE"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            stop.Cancel();
            if (thread != null && thread.IsAlive)
            {
                thread.Join(TimeSpan.FromSeconds(5));
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            stop.Cancel();
            stop.Dispose();
        }

        public StockQuoteSnapshot GetSnapshot()
        {
            lock (stateGate)
            {
                return new StockQuoteSnapshot
                {
                    Connected = connected,
                    Revision = revision,
                    UpdatedAt = updatedAt,
                    Quotes = quotes.Values
                        .OrderBy(item => item.Order)
                        .ThenBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
                        .Select(item => new StockQuoteItem
                        {
                            Symbol = item.Symbol,
                            Name = item.Name,
                            Quantity = item.Quantity,
                            Deal = item.Deal,
                            DiffPercent = item.DiffPercent,
                            TotalVol = item.TotalVol,
                            BidPrice = item.BidPrice,
                            UpdatedAt = item.UpdatedAt
                        })
                        .ToArray()
                };
            }
        }

        public StockDebugSnapshot GetDebugSnapshot()
        {
            lock (stateGate)
            {
                return new StockDebugSnapshot
                {
                    Connected = connected,
                    QuoteCount = quotes.Count,
                    ReadySymbolCount = readySymbolCount,
                    Revision = revision,
                    ElapsedMs = lifetime.ElapsedMilliseconds,
                    Events = debugEvents.ToArray()
                };
            }
        }

        private void AddDebug(string area, string message, long? durationMs = null)
        {
            lock (stateGate)
            {
                debugEvents.Enqueue(new StockDebugEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    ElapsedMs = lifetime.ElapsedMilliseconds,
                    Area = area,
                    Message = message,
                    DurationMs = durationMs
                });
                while (debugEvents.Count > DebugEventLimit) debugEvents.Dequeue();
            }
        }

        private void Run()
        {
            callback = Callback;
            AddDebug("host", "Mitake DDE thread starting");
            var initWatch = Stopwatch.StartNew();
            var initResult = DdeInitialize(ref instanceId, callback, AppCmdClientOnly, 0);
            initWatch.Stop();
            AddDebug("dde", $"DdeInitialize result={initResult}", initWatch.ElapsedMilliseconds);
            if (initResult != 0) return;

            IntPtr serviceHandle = IntPtr.Zero;
            IntPtr topicHandle = IntPtr.Zero;
            IntPtr conversation = IntPtr.Zero;
            var activeItemHandles = new List<IntPtr>();
            var pendingSubscriptions = new List<PendingSubscription>();
            var subscribedSignature = string.Empty;
            var nextPortfolioCheck = DateTimeOffset.MinValue;
            var nextConnectAttempt = DateTimeOffset.MinValue;

            try
            {
                serviceHandle = DdeCreateStringHandle(instanceId, ServiceName, CpWinAnsi);
                topicHandle = DdeCreateStringHandle(instanceId, TopicName, CpWinAnsi);

                while (!stop.IsCancellationRequested)
                {
                    PumpMessages();

                    var now = DateTimeOffset.UtcNow;
                    if (now >= nextPortfolioCheck)
                    {
                        var portfolioWatch = Stopwatch.StartNew();
                        var holdings = LoadHoldings();
                        portfolioWatch.Stop();
                        var signature = BuildSignature(holdings);
                        ApplyHoldings(holdings);
                        AddDebug("portfolio", $"loaded={holdings.Count}", portfolioWatch.ElapsedMilliseconds);

                        if (!string.Equals(signature, subscribedSignature, StringComparison.Ordinal))
                        {
                            ClearSubscriptions(conversation, activeItemHandles, pendingSubscriptions);
                            if (conversation != IntPtr.Zero)
                            {
                                DdeDisconnect(conversation);
                                conversation = IntPtr.Zero;
                            }
                            SetConnected(false);
                            subscribedSignature = signature;
                            nextConnectAttempt = DateTimeOffset.MinValue;
                        }

                        nextPortfolioCheck = now.AddSeconds(5);
                    }

                    if (ddeDisconnected)
                    {
                        ddeDisconnected = false;
                        ClearSubscriptions(conversation, activeItemHandles, pendingSubscriptions);
                        if (conversation != IntPtr.Zero)
                        {
                            DdeDisconnect(conversation);
                            conversation = IntPtr.Zero;
                        }
                        SetConnected(false);
                        nextConnectAttempt = now.AddSeconds(2);
                    }

                    if (conversation == IntPtr.Zero && HasQuotes() && now >= nextConnectAttempt)
                    {
                        var connectWatch = Stopwatch.StartNew();
                        conversation = DdeConnect(instanceId, serviceHandle, topicHandle, IntPtr.Zero);
                        connectWatch.Stop();
                        if (conversation != IntPtr.Zero)
                        {
                            SetConnected(true);
                            PrepareSubscriptions(pendingSubscriptions);
                            AddDebug("dde", $"DdeConnect ok pending={pendingSubscriptions.Count}", connectWatch.ElapsedMilliseconds);
                        }
                        else
                        {
                            SetConnected(false);
                            AddDebug("dde", $"DdeConnect failed error={DdeGetLastError(instanceId)}", connectWatch.ElapsedMilliseconds);
                            nextConnectAttempt = now.AddSeconds(2);
                        }
                    }

                    if (conversation != IntPtr.Zero && pendingSubscriptions.Count > 0)
                    {
                        ProcessSubscriptionBatch(conversation, activeItemHandles, pendingSubscriptions, now);
                    }

                    PublishIfNeeded(now);
                    Thread.Sleep(5);
                }
            }
            finally
            {
                ClearSubscriptions(conversation, activeItemHandles, pendingSubscriptions);
                if (conversation != IntPtr.Zero) DdeDisconnect(conversation);
                if (serviceHandle != IntPtr.Zero) DdeFreeStringHandle(instanceId, serviceHandle);
                if (topicHandle != IntPtr.Zero) DdeFreeStringHandle(instanceId, topicHandle);
                if (instanceId != 0) DdeUninitialize(instanceId);
                SetConnected(false);
                GC.KeepAlive(callback);
            }
        }

        private List<Holding> LoadHoldings()
        {
            var merged = new Dictionary<string, Holding>(StringComparer.OrdinalIgnoreCase);
            var order = 0;
            if (!Directory.Exists(portfoliosDirectory)) return new List<Holding>();

            foreach (var file in Directory.EnumerateFiles(portfoliosDirectory, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
                    if (!document.RootElement.TryGetProperty("positions", out var positions) || positions.ValueKind != JsonValueKind.Array) continue;

                    foreach (var position in positions.EnumerateArray())
                    {
                        var symbol = position.TryGetProperty("symbol", out var symbolNode) ? symbolNode.GetString()?.Trim() : null;
                        if (string.IsNullOrWhiteSpace(symbol)) continue;

                        var name = position.TryGetProperty("name", out var nameNode) ? nameNode.GetString()?.Trim() ?? string.Empty : string.Empty;
                        var quantity = ReadDecimal(position, "quantity");
                        if (quantity == 0) continue;

                        if (!merged.TryGetValue(symbol, out var holding))
                        {
                            holding = new Holding(symbol.ToUpperInvariant(), name, quantity, order++);
                            merged[symbol] = holding;
                        }
                        else
                        {
                            holding.Quantity += quantity;
                            if (string.IsNullOrWhiteSpace(holding.Name) && !string.IsNullOrWhiteSpace(name)) holding.Name = name;
                        }
                    }
                }
                catch
                {
                    // One malformed broker file must not stop live quotes from other portfolios.
                }
            }

            return merged.Values.Where(item => item.Quantity != 0).OrderBy(item => item.Order).ToList();
        }

        private static decimal ReadDecimal(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var node)) return 0;
            if (node.ValueKind == JsonValueKind.Number && node.TryGetDecimal(out var number)) return number;
            if (node.ValueKind == JsonValueKind.String && decimal.TryParse(node.GetString(), out number)) return number;
            return 0;
        }

        private void ApplyHoldings(IReadOnlyList<Holding> holdings)
        {
            lock (stateGate)
            {
                var desired = new HashSet<string>(holdings.Select(item => item.Symbol), StringComparer.OrdinalIgnoreCase);
                foreach (var removed in quotes.Keys.Where(symbol => !desired.Contains(symbol)).ToArray()) quotes.Remove(removed);

                foreach (var holding in holdings)
                {
                    if (!quotes.TryGetValue(holding.Symbol, out var quote))
                    {
                        quote = new MutableQuote { Symbol = holding.Symbol };
                        quotes[holding.Symbol] = quote;
                    }
                    quote.Name = string.IsNullOrWhiteSpace(quote.Name) ? holding.Name : quote.Name;
                    quote.Quantity = holding.Quantity;
                    quote.Order = holding.Order;
                }

                readySymbolCount = quotes.Values.Count(item => item.IsReady);
            }
        }

        private static string BuildSignature(IReadOnlyList<Holding> holdings)
        {
            return string.Join("|", holdings.Select(item => item.Symbol));
        }

        private bool HasQuotes()
        {
            lock (stateGate) return quotes.Count > 0;
        }

        private void PrepareSubscriptions(ICollection<PendingSubscription> pendingSubscriptions)
        {
            string[] symbols;
            lock (stateGate) symbols = quotes.Values.OrderBy(item => item.Order).Select(item => item.Symbol).ToArray();

            foreach (var symbol in symbols)
            {
                foreach (var field in QuoteFields)
                {
                    var item = $"{symbol} {field}";
                    var itemHandle = DdeCreateStringHandle(instanceId, item, CpWinAnsi);
                    if (itemHandle == IntPtr.Zero) continue;
                    pendingSubscriptions.Add(new PendingSubscription(itemHandle, item));
                }
            }
        }

        private void ProcessSubscriptionBatch(
            IntPtr conversation,
            ICollection<IntPtr> activeItemHandles,
            IList<PendingSubscription> pendingSubscriptions,
            DateTimeOffset now)
        {
            var processed = 0;
            var succeeded = 0;
            var failed = 0;
            uint firstError = 0;
            string failedItem = null;
            var batchWatch = Stopwatch.StartNew();
            for (var index = 0; index < pendingSubscriptions.Count && processed < SubscriptionBatchSize;)
            {
                var pending = pendingSubscriptions[index];
                if (pending.NextAttemptAt > now)
                {
                    index++;
                    continue;
                }

                processed++;
                uint transactionResult;
                var timeoutMs = activeItemHandles.Count == 0
                    ? WarmupSubscriptionTimeoutMs
                    : SubscriptionTimeoutMs;
                var result = DdeClientTransaction(
                    IntPtr.Zero,
                    0,
                    conversation,
                    pending.ItemHandle,
                    CfText,
                    XtypAdvStart,
                    (uint)timeoutMs,
                    out transactionResult);

                if (result != IntPtr.Zero)
                {
                    activeItemHandles.Add(pending.ItemHandle);
                    pendingSubscriptions.RemoveAt(index);
                    succeeded++;
                    continue;
                }

                failed++;
                if (failedItem == null)
                {
                    firstError = DdeGetLastError(instanceId);
                    failedItem = pending.Item;
                }
                pending.Attempts++;
                var backoffMs = Math.Min(5000, 250 * (1 << Math.Min(pending.Attempts, 4)));
                pending.NextAttemptAt = now.AddMilliseconds(backoffMs);
                index++;
                if (ddeDisconnected) break;
            }

            batchWatch.Stop();
            if (processed > 0)
            {
                AddDebug(
                    "subscribe",
                    $"processed={processed} ok={succeeded} failed={failed} active={activeItemHandles.Count} pending={pendingSubscriptions.Count} firstError={firstError} firstItem={failedItem ?? "-"}",
                    batchWatch.ElapsedMilliseconds);
            }
        }

        private void ClearSubscriptions(
            IntPtr conversation,
            ICollection<IntPtr> activeItemHandles,
            ICollection<PendingSubscription> pendingSubscriptions)
        {
            StopSubscriptions(conversation, activeItemHandles);
            activeItemHandles.Clear();
            foreach (var pending in pendingSubscriptions)
            {
                DdeFreeStringHandle(instanceId, pending.ItemHandle);
            }
            pendingSubscriptions.Clear();
        }

        private void StopSubscriptions(IntPtr conversation, IEnumerable<IntPtr> itemHandles)
        {
            foreach (var itemHandle in itemHandles)
            {
                if (conversation != IntPtr.Zero)
                {
                    uint transactionResult;
                    DdeClientTransaction(IntPtr.Zero, 0, conversation, itemHandle, CfText, XtypAdvStop, StopTimeoutMs, out transactionResult);
                }
                DdeFreeStringHandle(instanceId, itemHandle);
            }
        }

        private void PublishIfNeeded(DateTimeOffset now)
        {
            if (!dirty || now < nextPublishAt) return;

            long currentRevision;
            lock (stateGate)
            {
                dirty = false;
                currentRevision = revision;
            }

            eventHub.Publish("stock-quotes", new { revision = currentRevision });
            nextPublishAt = now.AddMilliseconds(350);
        }

        private void SetConnected(bool value)
        {
            lock (stateGate)
            {
                if (connected == value) return;
                connected = value;
                revision++;
                updatedAt = DateTimeOffset.UtcNow;
                dirty = true;
            }
        }

        private IntPtr Callback(
            uint transactionType,
            uint format,
            IntPtr conversation,
            IntPtr stringHandle1,
            IntPtr stringHandle2,
            IntPtr dataHandle,
            UIntPtr data1,
            UIntPtr data2)
        {
            if ((transactionType & 0xFFFF) == XtypDisconnect)
            {
                ddeDisconnected = true;
                AddDebug("dde", "XTYP_DISCONNECT callback");
                return IntPtr.Zero;
            }

            if ((transactionType & 0xFFFF) != XtypAdvData || dataHandle == IntPtr.Zero) return IntPtr.Zero;

            var item = QueryString(stringHandle2);
            var value = DecodeDataHandle(dataHandle);
            if (string.IsNullOrWhiteSpace(item) || value == null) return new IntPtr(DdeFAck);

            var separatorIndex = item.IndexOf(' ');
            if (separatorIndex <= 0 || separatorIndex >= item.Length - 1) return new IntPtr(DdeFAck);

            var symbol = item.Substring(0, separatorIndex);
            var field = item.Substring(separatorIndex + 1);
            string firstDataMessage = null;
            string readyMessage = null;
            lock (stateGate)
            {
                if (!quotes.TryGetValue(symbol, out var quote)) return new IntPtr(DdeFAck);

                var wasReady = quote.IsReady;

                switch (field)
                {
                    case "Name": quote.Name = value; break;
                    case "Deal": quote.Deal = value; break;
                    case "DiffPercent": quote.DiffPercent = value; break;
                    case "TotalVol": quote.TotalVol = value; break;
                    case "BidPrice": quote.BidPrice = value; break;
                }

                quote.UpdatedAt = DateTimeOffset.UtcNow;
                if (!firstAdvDataLogged)
                {
                    firstAdvDataLogged = true;
                    firstDataMessage = $"first ADVDATA item={item}";
                }
                if (!wasReady && quote.IsReady)
                {
                    readySymbolCount++;
                    readyMessage = $"{readySymbolCount}/{quotes.Count} symbol={symbol}";
                }
                updatedAt = quote.UpdatedAt;
                revision++;
                dirty = true;
            }

            if (firstDataMessage != null) AddDebug("dde", firstDataMessage);
            if (readyMessage != null) AddDebug("ready", readyMessage);

            return new IntPtr(DdeFAck);
        }

        private string QueryString(IntPtr stringHandle)
        {
            var buffer = new StringBuilder(256);
            var length = DdeQueryString(instanceId, stringHandle, buffer, buffer.Capacity, CpWinAnsi);
            return length == 0 ? string.Empty : buffer.ToString();
        }

        private static string DecodeDataHandle(IntPtr dataHandle)
        {
            uint dataSize = 0;
            var pointer = DdeAccessData(dataHandle, ref dataSize);
            if (pointer == IntPtr.Zero || dataSize == 0) return null;

            try
            {
                var bytes = new byte[dataSize];
                Marshal.Copy(pointer, bytes, 0, bytes.Length);
                bytes = UnwrapMitakePayload(bytes);
                return Encoding.GetEncoding(950).GetString(bytes).TrimEnd('\0', '\r', '\n', ' ', '\t');
            }
            finally
            {
                DdeUnaccessData(dataHandle);
            }
        }

        private static byte[] UnwrapMitakePayload(byte[] bytes)
        {
            if (bytes.Length < 13) return bytes;
            var outerLength = BitConverter.ToUInt16(bytes, 10);
            var payloadLength = bytes[12];
            if (outerLength != payloadLength + 1 || 13 + payloadLength > bytes.Length) return bytes;
            return bytes.AsSpan(13, payloadLength).ToArray();
        }

        private static void PumpMessages()
        {
            while (PeekMessage(out var message, IntPtr.Zero, 0, 0, PmRemove))
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }

        private sealed class Holding
        {
            public Holding(string symbol, string name, decimal quantity, int order)
            {
                Symbol = symbol;
                Name = name;
                Quantity = quantity;
                Order = order;
            }

            public string Symbol { get; }
            public string Name { get; set; }
            public decimal Quantity { get; set; }
            public int Order { get; }
        }

        private sealed class MutableQuote
        {
            public string Symbol { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public decimal Quantity { get; set; }
            public string Deal { get; set; } = string.Empty;
            public string DiffPercent { get; set; } = string.Empty;
            public string TotalVol { get; set; } = string.Empty;
            public string BidPrice { get; set; } = string.Empty;
            public DateTimeOffset? UpdatedAt { get; set; }
            public int Order { get; set; }
            public bool IsReady =>
                !string.IsNullOrWhiteSpace(Deal) &&
                !string.IsNullOrWhiteSpace(DiffPercent) &&
                !string.IsNullOrWhiteSpace(TotalVol) &&
                !string.IsNullOrWhiteSpace(BidPrice);
        }

        public sealed class StockDebugSnapshot
        {
            public bool Connected { get; set; }
            public int QuoteCount { get; set; }
            public int ReadySymbolCount { get; set; }
            public long Revision { get; set; }
            public long ElapsedMs { get; set; }
            public StockDebugEvent[] Events { get; set; } = Array.Empty<StockDebugEvent>();
        }

        public sealed class StockDebugEvent
        {
            public DateTimeOffset Timestamp { get; set; }
            public long ElapsedMs { get; set; }
            public string Area { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public long? DurationMs { get; set; }
        }

        private sealed class PendingSubscription
        {
            public PendingSubscription(IntPtr itemHandle, string item)
            {
                ItemHandle = itemHandle;
                Item = item;
            }

            public IntPtr ItemHandle { get; }
            public string Item { get; }
            public int Attempts { get; set; }
            public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.MinValue;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Message
        {
            public IntPtr HWnd;
            public uint MessageId;
            public UIntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public Point Point;
            public uint Private;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr DdeCallback(
            uint transactionType,
            uint format,
            IntPtr conversation,
            IntPtr stringHandle1,
            IntPtr stringHandle2,
            IntPtr dataHandle,
            UIntPtr data1,
            UIntPtr data2);

        [DllImport("user32.dll", EntryPoint = "DdeInitializeA", ExactSpelling = true)]
        private static extern uint DdeInitialize(ref uint instanceId, DdeCallback callback, uint command, uint reserved);

        [DllImport("user32.dll")]
        private static extern bool DdeUninitialize(uint instanceId);

        [DllImport("user32.dll", EntryPoint = "DdeCreateStringHandleA", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr DdeCreateStringHandle(uint instanceId, string value, int codePage);

        [DllImport("user32.dll")]
        private static extern bool DdeFreeStringHandle(uint instanceId, IntPtr stringHandle);

        [DllImport("user32.dll")]
        private static extern IntPtr DdeConnect(uint instanceId, IntPtr service, IntPtr topic, IntPtr context);

        [DllImport("user32.dll")]
        private static extern bool DdeDisconnect(IntPtr conversation);

        [DllImport("user32.dll")]
        private static extern uint DdeGetLastError(uint instanceId);

        [DllImport("user32.dll")]
        private static extern IntPtr DdeClientTransaction(
            IntPtr data,
            uint dataLength,
            IntPtr conversation,
            IntPtr item,
            uint format,
            uint transactionType,
            uint timeout,
            out uint transactionResult);

        [DllImport("user32.dll", EntryPoint = "DdeQueryStringA", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern uint DdeQueryString(uint instanceId, IntPtr stringHandle, StringBuilder buffer, int maxLength, int codePage);

        [DllImport("user32.dll")]
        private static extern IntPtr DdeAccessData(IntPtr dataHandle, ref uint dataSize);

        [DllImport("user32.dll")]
        private static extern bool DdeUnaccessData(IntPtr dataHandle);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out Message message, IntPtr window, uint minMessage, uint maxMessage, uint removeMessage);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref Message message);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref Message message);
    }
}
