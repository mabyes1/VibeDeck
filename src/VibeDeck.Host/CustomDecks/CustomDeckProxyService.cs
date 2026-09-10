using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using VibeDeck.Host.Security;

namespace VibeDeck.Host.CustomDecks
{
    public sealed class CustomDeckProxyService : IDisposable
    {
        private static readonly HashSet<string> RequestHeadersToSkip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Connection", "Content-Length", "Cookie", "Host", "Origin", "Referer", "Transfer-Encoding", "Upgrade"
        };
        private static readonly HashSet<string> ResponseHeadersToSkip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Connection", "Content-Encoding", "Content-Length", "Content-Security-Policy", "Keep-Alive",
            "Proxy-Authenticate", "Proxy-Authorization", "Set-Cookie", "Trailer", "Transfer-Encoding",
            "Upgrade", "X-Frame-Options"
        };
        private static readonly Regex RootAttribute = new Regex(
            "(?<head>\\b(?:href|src|action|poster)\\s*=\\s*(?<quote>[\\\"']))/(?<path>(?!/)[^\\\"']*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RootCssUrl = new Regex(
            "url\\(\\s*(?<quote>[\\\"']?)/(?<path>(?!/)[^)\\\"']+)(?:\\k<quote>)?\\s*\\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly ConcurrentDictionary<string, ProxySession> sessions = new ConcurrentDictionary<string, ProxySession>();

        public async Task ProxyAsync(HttpContext context, CustomDeckDescriptor deck, string path)
        {
            var baseUri = CustomDeckProxyTarget.Parse(deck.ProxyTargetUrl);
            var target = BuildTarget(baseUri, path, context.Request.QueryString.Value);
            var session = GetSession(context, deck.Id, baseUri);

            if (context.WebSockets.IsWebSocketRequest)
            {
                await ProxyWebSocketAsync(context, session, target);
                return;
            }

            using var upstreamRequest = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
            CopyRequestHeaders(context, upstreamRequest, baseUri);
            if (CanHaveBody(context.Request.Method) &&
                (context.Request.ContentLength.GetValueOrDefault() > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding")))
            {
                upstreamRequest.Content = new StreamContent(context.Request.Body);
                if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
                {
                    upstreamRequest.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
                }
            }

            using var upstream = await session.Client.SendAsync(
                upstreamRequest,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);

            context.Response.StatusCode = (int)upstream.StatusCode;
            CopyResponseHeaders(context, upstream, deck, target);
            context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
            context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'self'; object-src 'none'; base-uri 'self'";
            context.Response.Headers["Cache-Control"] = "no-store";

            if (upstream.Content == null) return;
            var mediaType = upstream.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (IsRewritable(mediaType))
            {
                var text = await upstream.Content.ReadAsStringAsync(context.RequestAborted);
                var rewritten = RewriteText(text, mediaType, deck, baseUri);
                context.Response.ContentLength = Encoding.UTF8.GetByteCount(rewritten);
                await context.Response.WriteAsync(rewritten, Encoding.UTF8, context.RequestAborted);
                return;
            }

            await upstream.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }

        internal static string RewriteText(string value, string mediaType, CustomDeckDescriptor deck, Uri baseUri)
        {
            var prefix = $"/deck-proxy/{Uri.EscapeDataString(deck.Id)}/";
            var text = value
                .Replace(baseUri.GetLeftPart(UriPartial.Authority) + "/", prefix, StringComparison.OrdinalIgnoreCase);

            if (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                text = RootAttribute.Replace(text, match =>
                    match.Groups["head"].Value + prefix + match.Groups["path"].Value);
                var injection = $"<base href=\"{prefix}\">{BuildBridgeScript(prefix, baseUri)}{CustomDeckViewerBridge.Script}";
                var head = text.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
                var close = head >= 0 ? text.IndexOf('>', head) : -1;
                text = close >= 0 ? text.Insert(close + 1, injection) : injection + text;
            }
            else if (mediaType.Contains("css", StringComparison.OrdinalIgnoreCase))
            {
                text = RootCssUrl.Replace(text, match => $"url({match.Groups["quote"].Value}{prefix}{match.Groups["path"].Value}{match.Groups["quote"].Value})");
            }

            return text;
        }

        internal static Uri BuildTarget(Uri baseUri, string path, string query)
        {
            var relative = (path ?? string.Empty).TrimStart('/');
            var target = new Uri(baseUri, relative + (query ?? string.Empty));
            if (!string.Equals(target.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(target.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) ||
                target.Port != baseUri.Port)
            {
                throw new InvalidDataException("Proxy Deck request escaped its configured origin.");
            }
            return target;
        }

        private ProxySession GetSession(HttpContext context, string deckId, Uri baseUri)
        {
            var sessionId = BuildSessionIdentity(context);
            return sessions.GetOrAdd($"{deckId}:{sessionId}", _ => new ProxySession(baseUri));
        }

        internal static string BuildSessionIdentity(HttpContext context)
        {
            var identity = context.Request.Headers[DeviceTrustService.HeaderName].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(identity))
                identity = context.Request.Cookies[DeviceTrustService.CookieName];
            if (string.IsNullOrWhiteSpace(identity))
                identity = context.Request.Cookies[HostAccessAuthService.CookieName];
            if (string.IsNullOrWhiteSpace(identity))
            {
                identity = string.Join("|",
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    context.Request.Headers.UserAgent.ToString());
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        }

        private static void CopyRequestHeaders(HttpContext context, HttpRequestMessage target, Uri baseUri)
        {
            foreach (var header in context.Request.Headers)
            {
                if (!RequestHeadersToSkip.Contains(header.Key))
                    target.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
            var origin = baseUri.GetLeftPart(UriPartial.Authority);
            target.Headers.TryAddWithoutValidation("Origin", origin);
            target.Headers.Referrer = baseUri;
        }

        private static void CopyResponseHeaders(HttpContext context, HttpResponseMessage upstream, CustomDeckDescriptor deck, Uri target)
        {
            foreach (var header in upstream.Headers.Concat(upstream.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()))
            {
                if (ResponseHeadersToSkip.Contains(header.Key)) continue;
                if (string.Equals(header.Key, "Location", StringComparison.OrdinalIgnoreCase))
                {
                    var location = header.Value.FirstOrDefault();
                    if (Uri.TryCreate(target, location, out var redirect) &&
                        string.Equals(redirect.Scheme, target.Scheme, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(redirect.Host, target.Host, StringComparison.OrdinalIgnoreCase) &&
                        redirect.Port == target.Port)
                    {
                        var prefix = $"/deck-proxy/{Uri.EscapeDataString(deck.Id)}/";
                        context.Response.Headers.Location = prefix + redirect.PathAndQuery.TrimStart('/');
                    }
                    continue;
                }
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        private static bool CanHaveBody(string method) =>
            !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsTrace(method);

        private static bool IsRewritable(string mediaType) =>
            mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Contains("javascript", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Contains("svg", StringComparison.OrdinalIgnoreCase);

    private static string BuildBridgeScript(string prefix, Uri baseUri)
    {
        var prefixJson = JsonSerializer.Serialize(prefix);
        var upstreamJson = JsonSerializer.Serialize(baseUri.GetLeftPart(UriPartial.Authority));
        return $$"""
<script>(() => {
  const prefix = {{prefixJson}}, upstream = {{upstreamJson}};
  const map = value => {
    if (typeof value !== "string") return value;
    try {
      const url = new URL(value, upstream + "/");
      if (value.startsWith("/") || url.origin === upstream) {
        return location.origin + prefix + url.pathname.replace(/^\//, "") + url.search + url.hash;
      }
    } catch {}
    return value;
  };
  const nativeFetch = window.fetch;
  window.fetch = (input, init) => nativeFetch.call(window, typeof input === "string" ? map(input) : input, init);
  const nativeOpen = XMLHttpRequest.prototype.open;
  XMLHttpRequest.prototype.open = function(method, url, ...rest) { return nativeOpen.call(this, method, map(url), ...rest); };
  const NativeEventSource = window.EventSource;
  if (NativeEventSource) window.EventSource = function(url, options) { return new NativeEventSource(map(url), options); };
  const NativeWebSocket = window.WebSocket;
  if (NativeWebSocket) window.WebSocket = function(url, protocols) {
    const mapped = new URL(map(String(url)));
    mapped.protocol = location.protocol === "https:" ? "wss:" : "ws:";
    return protocols === undefined ? new NativeWebSocket(mapped) : new NativeWebSocket(mapped, protocols);
  };
})();</script>
""";
    }

        private static async Task ProxyWebSocketAsync(HttpContext context, ProxySession session, Uri target)
        {
            var builder = new UriBuilder(target) { Scheme = target.Scheme == Uri.UriSchemeHttps ? "wss" : "ws" };
            using var upstream = new ClientWebSocket();
            upstream.Options.Cookies = session.Cookies;
            var protocols = context.Request.Headers["Sec-WebSocket-Protocol"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var protocol in protocols) upstream.Options.AddSubProtocol(protocol);
            await upstream.ConnectAsync(builder.Uri, context.RequestAborted);
            using var client = string.IsNullOrWhiteSpace(upstream.SubProtocol)
                ? await context.WebSockets.AcceptWebSocketAsync()
                : await context.WebSockets.AcceptWebSocketAsync(upstream.SubProtocol);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            var upstreamToClient = PumpWebSocketAsync(upstream, client, cancellation.Token);
            var clientToUpstream = PumpWebSocketAsync(client, upstream, cancellation.Token);
            await Task.WhenAny(upstreamToClient, clientToUpstream);
            cancellation.Cancel();
            try { await Task.WhenAll(upstreamToClient, clientToUpstream); } catch (OperationCanceledException) { }
        }

        private static async Task PumpWebSocketAsync(WebSocket source, WebSocket destination, CancellationToken cancellationToken)
        {
            var buffer = new byte[64 * 1024];
            while (!cancellationToken.IsCancellationRequested && source.State == WebSocketState.Open)
            {
                var result = await source.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    if (destination.State == WebSocketState.Open)
                        await destination.CloseOutputAsync(result.CloseStatus ?? WebSocketCloseStatus.NormalClosure, result.CloseStatusDescription, cancellationToken);
                    return;
                }
                await destination.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, cancellationToken);
            }
        }

        public void Dispose()
        {
            foreach (var session in sessions.Values) session.Dispose();
            sessions.Clear();
        }

        private sealed class ProxySession : IDisposable
        {
            public ProxySession(Uri baseUri)
            {
                Cookies = new CookieContainer();
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                    CookieContainer = Cookies,
                    UseCookies = true
                };
                Client = new HttpClient(handler, disposeHandler: true)
                {
                    BaseAddress = baseUri,
                    Timeout = TimeSpan.FromSeconds(30)
                };
            }

            public CookieContainer Cookies { get; }
            public HttpClient Client { get; }
            public void Dispose() => Client.Dispose();
        }
    }
}
