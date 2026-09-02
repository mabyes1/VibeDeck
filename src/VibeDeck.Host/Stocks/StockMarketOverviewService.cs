using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace VibeDeck.Host.Stocks
{
    public sealed class StockMarketOverviewService : BackgroundService
    {
        private const string TwseUrl = "https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=tse_t00.tw&json=1&delay=0";
        private const string TaifexUrl = "https://mis.bq888.taifex.com.tw/futures/api/getQuoteList";
        private readonly IHttpClientFactory httpClientFactory;
        private readonly object stateGate = new object();
        private StockMarketQuoteItem[] markets = Array.Empty<StockMarketQuoteItem>();

        public StockMarketOverviewService(IHttpClientFactory httpClientFactory)
        {
            this.httpClientFactory = httpClientFactory;
        }

        public IReadOnlyList<StockMarketQuoteItem> GetSnapshot()
        {
            lock (stateGate)
            {
                var now = DateTimeOffset.Now;
                return markets.Select(item => new StockMarketQuoteItem
                {
                    Symbol = item.Symbol,
                    Name = item.Name,
                    Deal = item.Deal,
                    Change = item.Change,
                    DiffPercent = item.DiffPercent,
                    Source = item.Source,
                    UpdatedAt = item.UpdatedAt,
                    Stale = !item.UpdatedAt.HasValue || now - item.UpdatedAt.Value > TimeSpan.FromMinutes(3)
                }).ToArray();
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var twseTask = ReadTwseAsync(stoppingToken);
                    var taifexTask = ReadTaifexAsync(stoppingToken);
                    await Task.WhenAll(twseTask, taifexTask);
                    var next = new[] { twseTask.Result, taifexTask.Result }.Where(item => item != null).ToArray();
                    if (next.Length > 0)
                    {
                        lock (stateGate) markets = next;
                    }
                }
                catch when (!stoppingToken.IsCancellationRequested)
                {
                    // Keep the last known values. GetSnapshot marks them stale when needed.
                }

                try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
                catch (OperationCanceledException) { }
            }
        }

        private async Task<StockMarketQuoteItem> ReadTwseAsync(CancellationToken cancellationToken)
        {
            var client = httpClientFactory.CreateClient("stock-market");
            using var request = new HttpRequestMessage(HttpMethod.Get, TwseUrl);
            request.Headers.Referrer = new Uri("https://mis.twse.com.tw/stock/index.jsp");
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var quote = document.RootElement.GetProperty("msgArray").EnumerateArray().First();
            var deal = ReadText(quote, "z");
            var previous = ReadDecimal(quote, "y");
            var current = ParseDecimal(deal);
            var change = current.HasValue && previous.HasValue ? current.Value - previous.Value : (decimal?)null;
            var percent = change.HasValue && previous.HasValue && previous.Value != 0
                ? change.Value / previous.Value * 100
                : (decimal?)null;
            return new StockMarketQuoteItem
            {
                Symbol = "TAIEX",
                Name = "加權指數",
                Deal = deal,
                Change = FormatSigned(change, 2),
                DiffPercent = FormatSigned(percent, 2),
                Source = "twse-mis",
                UpdatedAt = ReadTaipeiTime(ReadText(quote, "d"), ReadText(quote, "t"))
            };
        }

        private async Task<StockMarketQuoteItem> ReadTaifexAsync(CancellationToken cancellationToken)
        {
            var regular = ReadTaifexSessionAsync("0", cancellationToken);
            var afterHours = ReadTaifexSessionAsync("1", cancellationToken);
            var sessions = await Task.WhenAll(regular, afterHours);
            return sessions.Where(item => item != null)
                .OrderByDescending(item => item.UpdatedAt)
                .ThenByDescending(item => item.Volume)
                .Select(item => item.Quote)
                .FirstOrDefault();
        }

        private async Task<TaifexCandidate> ReadTaifexSessionAsync(string marketType, CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(new
            {
                MarketType = marketType,
                SymbolType = "F",
                KindID = "1",
                CID = "TXF",
                ExpireMonth = "",
                RowSize = "全部",
                PageNo = "",
                SortColumn = "",
                AscDesc = "A"
            });
            var client = httpClientFactory.CreateClient("stock-market");
            using var request = new HttpRequestMessage(HttpMethod.Post, TaifexUrl)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Referrer = new Uri("https://mis.taifex.com.tw/futures/");
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (ReadText(document.RootElement, "RtCode") != "0") return null;
            var list = document.RootElement.GetProperty("RtData").GetProperty("QuoteList").EnumerateArray();
            return list
                .Where(item => ReadText(item, "SymbolID") != "TXF-S")
                .Select(item =>
                {
                    var stamp = ReadTaipeiTime(ReadText(item, "CDate"), ReadText(item, "CTime"));
                    var volume = ParseDecimal(ReadText(item, "CTotalVolume")) ?? 0;
                    return new TaifexCandidate
                    {
                        UpdatedAt = stamp,
                        Volume = volume,
                        Quote = new StockMarketQuoteItem
                        {
                            Symbol = ReadText(item, "SymbolID"),
                            Name = "台指近月",
                            Deal = TrimPrice(ReadText(item, "CLastPrice")),
                            Change = TrimPrice(ReadText(item, "CDiff")),
                            DiffPercent = TrimPrice(ReadText(item, "CDiffRate")),
                            Source = "taifex-mis",
                            UpdatedAt = stamp
                        }
                    };
                })
                .OrderByDescending(item => item.Volume)
                .FirstOrDefault();
        }

        private static string ReadText(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

        private static decimal? ReadDecimal(JsonElement element, string property) => ParseDecimal(ReadText(element, property));

        private static decimal? ParseDecimal(string value) =>
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : (decimal?)null;

        private static string FormatSigned(decimal? value, int decimals) => !value.HasValue
            ? string.Empty
            : value.Value.ToString($"+0.{new string('0', decimals)};-0.{new string('0', decimals)};0.{new string('0', decimals)}", CultureInfo.InvariantCulture);

        private static string TrimPrice(string value) => ParseDecimal(value)?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;

        private static DateTimeOffset? ReadTaipeiTime(string date, string time)
        {
            var digits = (time ?? string.Empty).Replace(":", string.Empty).PadLeft(6, '0');
            if (!DateTime.TryParseExact(date + digits, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return null;
            return new DateTimeOffset(local, TimeSpan.FromHours(8));
        }

        private sealed class TaifexCandidate
        {
            public StockMarketQuoteItem Quote { get; set; }
            public DateTimeOffset? UpdatedAt { get; set; }
            public decimal Volume { get; set; }
        }
    }
}
