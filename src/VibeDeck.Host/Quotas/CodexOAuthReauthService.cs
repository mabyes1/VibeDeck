using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VibeDeck.Host.Quotas
{
    /// <summary>
    /// Runs the ChatGPT/Codex authorization-code + PKCE flow in-process.
    ///
    /// This deliberately does not spawn Codex or write ~/.codex/auth.json. The
    /// exchanged tokens live only in this process until CodexAccountStore verifies
    /// that the authorized identity matches the selected quota profile.
    /// </summary>
    internal static class CodexOAuthReauthService
    {
        internal const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
        internal const string Issuer = "https://auth.openai.com";
        internal const string Scopes = "openid profile email offline_access api.connectors.read api.connectors.invoke";
        internal const string Originator = "codex_cli_rs";
        internal const int DefaultCallbackPort = 1455;
        internal const int FallbackCallbackPort = 1457;

        private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(12);
        private static readonly object SessionLock = new object();
        private static readonly Dictionary<string, OAuthSession> Sessions =
            new Dictionary<string, OAuthSession>(StringComparer.OrdinalIgnoreCase);
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        internal sealed class StartResult
        {
            public bool Success { get; set; }
            public string SessionId { get; set; }
            public DateTimeOffset? ExpiresAt { get; set; }
            public string Code { get; set; }
            public string Message { get; set; }
        }

        internal sealed class PollResult
        {
            public string State { get; set; }
            public string Code { get; set; }
            public string Message { get; set; }
            public OAuthTokens Tokens { get; set; }
        }

        internal sealed class OAuthTokens
        {
            public string IdToken { get; set; }
            public string AccessToken { get; set; }
            public string RefreshToken { get; set; }
        }

        private sealed class OAuthSession
        {
            public string SessionId { get; set; }
            public string State { get; set; }
            public string CodeVerifier { get; set; }
            public string RedirectUri { get; set; }
            public string AuthorizationUrl { get; set; }
            public DateTimeOffset ExpiresAt { get; set; }
            public TcpListener Listener { get; set; }
            public CancellationTokenSource Cancellation { get; set; }
            public PollResult Result { get; set; }
        }

        internal static StartResult Start()
        {
            var now = DateTimeOffset.UtcNow;
            lock (SessionLock)
            {
                PruneExpiredSessions(now);
            }

            var listener = TryBindCallbackListener(out var port);
            if (listener == null)
            {
                return new StartResult
                {
                    Success = false,
                    Code = "quota.codex_reauth_port_unavailable",
                    Message = "Codex authorization callback ports 1455 and 1457 are already in use."
                };
            }

            var sessionId = Guid.NewGuid().ToString("N");
            var state = GenerateBase64UrlToken();
            var verifier = GenerateBase64UrlToken();
            var challenge = BuildCodeChallenge(verifier);
            var redirectUri = $"http://localhost:{port}/auth/callback";
            var expiresAt = now.Add(SessionLifetime);
            var session = new OAuthSession
            {
                SessionId = sessionId,
                State = state,
                CodeVerifier = verifier,
                RedirectUri = redirectUri,
                AuthorizationUrl = BuildAuthorizationUrl(redirectUri, challenge, state),
                ExpiresAt = expiresAt,
                Listener = listener,
                Cancellation = new CancellationTokenSource(),
                Result = new PollResult { State = "pending" }
            };

            lock (SessionLock)
            {
                Sessions[sessionId] = session;
            }

            _ = RunCallbackServerAsync(session);
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = session.AuthorizationUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                CompleteSession(session, new PollResult
                {
                    State = "failed",
                    Code = "quota.codex_reauth_browser_open_failed",
                    Message = "VibeDeck could not open the Codex authorization page."
                });
                Forget(sessionId);
                return new StartResult
                {
                    Success = false,
                    Code = "quota.codex_reauth_browser_open_failed",
                    Message = "VibeDeck could not open the Codex authorization page."
                };
            }

            return new StartResult
            {
                Success = true,
                SessionId = sessionId,
                ExpiresAt = expiresAt,
                Message = "Complete Codex authorization in the opened browser window."
            };
        }

        internal static PollResult Poll(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Failed("quota.codex_reauth_session_required", "Authorization session is required.");
            }

            lock (SessionLock)
            {
                PruneExpiredSessions(DateTimeOffset.UtcNow);
                if (!Sessions.TryGetValue(sessionId, out var session))
                {
                    return Failed("quota.codex_reauth_session_not_found", "Codex authorization session was not found.");
                }

                return session.Result ?? new PollResult { State = "pending" };
            }
        }

        internal static void Forget(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return;
            lock (SessionLock)
            {
                if (!Sessions.TryGetValue(sessionId, out var session)) return;
                Sessions.Remove(sessionId);
                StopListener(session);
            }
        }

        internal static string BuildAuthorizationUrl(string redirectUri, string codeChallenge, string state)
        {
            var query = new Dictionary<string, string>
            {
                ["response_type"] = "code",
                ["client_id"] = ClientId,
                ["redirect_uri"] = redirectUri,
                ["scope"] = Scopes,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256",
                ["id_token_add_organizations"] = "true",
                ["codex_cli_simplified_flow"] = "true",
                ["state"] = state,
                ["originator"] = Originator
            };
            var encoded = string.Join("&", query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? string.Empty)}"));
            return $"{Issuer}/oauth/authorize?{encoded}";
        }

        internal static string BuildCodeChallenge(string verifier)
        {
            var digest = SHA256.HashData(Encoding.ASCII.GetBytes(verifier ?? string.Empty));
            return Base64UrlEncode(digest);
        }

        internal static IReadOnlyDictionary<string, string> ParseQuery(string requestTarget)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(requestTarget)) return result;
            var question = requestTarget.IndexOf('?');
            if (question < 0 || question >= requestTarget.Length - 1) return result;
            foreach (var pair in requestTarget.Substring(question + 1).Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                var key = WebUtility.UrlDecode(parts[0] ?? string.Empty);
                if (string.IsNullOrWhiteSpace(key)) continue;
                var value = parts.Length > 1 ? WebUtility.UrlDecode(parts[1]) : string.Empty;
                result[key] = value ?? string.Empty;
            }
            return result;
        }

        private static async Task RunCallbackServerAsync(OAuthSession session)
        {
            try
            {
                while (!session.Cancellation.IsCancellationRequested && DateTimeOffset.UtcNow < session.ExpiresAt)
                {
                    using var client = await session.Listener.AcceptTcpClientAsync(session.Cancellation.Token);
                    var request = await ReadRequestAsync(client, session.Cancellation.Token);
                    if (request == null) continue;

                    if (string.Equals(request.Target, "/cancel", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteBrowserResponseAsync(client, 200, "Authorization cancelled.", session.Cancellation.Token);
                        CompleteSession(session, Failed("quota.codex_reauth_cancelled", "Codex authorization was cancelled."));
                        return;
                    }

                    if (!request.Target.StartsWith("/auth/callback", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteBrowserResponseAsync(client, 404, "Not found.", session.Cancellation.Token);
                        continue;
                    }

                    var query = ParseQuery(request.Target);
                    if (!query.TryGetValue("state", out var callbackState) ||
                        !FixedTimeEquals(callbackState, session.State))
                    {
                        await WriteBrowserResponseAsync(client, 400, "Authorization state did not match. You can close this window and retry from VibeDeck.", session.Cancellation.Token);
                        continue;
                    }

                    if (query.TryGetValue("error", out var oauthError) && !string.IsNullOrWhiteSpace(oauthError))
                    {
                        var description = query.TryGetValue("error_description", out var detail) ? detail : oauthError;
                        await WriteBrowserResponseAsync(client, 400, "Codex authorization was not completed. You can close this window.", session.Cancellation.Token);
                        CompleteSession(session, Failed("quota.codex_reauth_denied", description));
                        return;
                    }

                    if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
                    {
                        await WriteBrowserResponseAsync(client, 400, "Authorization code was missing. You can close this window and retry from VibeDeck.", session.Cancellation.Token);
                        CompleteSession(session, Failed("quota.codex_reauth_callback_invalid", "Codex authorization callback did not contain a code."));
                        return;
                    }

                    var exchange = await ExchangeCodeAsync(code, session.RedirectUri, session.CodeVerifier, session.Cancellation.Token);
                    if (exchange.State == "complete")
                    {
                        await WriteBrowserResponseAsync(client, 200, "Codex authorization completed. You can close this window and return to VibeDeck.", session.Cancellation.Token);
                    }
                    else
                    {
                        await WriteBrowserResponseAsync(client, 500, "Codex authorization could not be completed. Return to VibeDeck for details.", session.Cancellation.Token);
                    }
                    CompleteSession(session, exchange);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException)
            {
                CompleteSession(session, Failed("quota.codex_reauth_callback_failed", "The local Codex authorization callback stopped unexpectedly."));
            }
        }

        private static async Task<PollResult> ExchangeCodeAsync(
            string code,
            string redirectUri,
            string verifier,
            CancellationToken cancellationToken)
        {
            try
            {
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri,
                    ["client_id"] = ClientId,
                    ["code_verifier"] = verifier
                });
                using var response = await HttpClient.PostAsync($"{Issuer}/oauth/token", content, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return Failed("quota.codex_reauth_token_exchange_failed", $"Codex token exchange returned HTTP {(int)response.StatusCode}.");
                }

                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                var idToken = ReadString(root, "id_token");
                var accessToken = ReadString(root, "access_token");
                var refreshToken = ReadString(root, "refresh_token");
                if (string.IsNullOrWhiteSpace(idToken) ||
                    string.IsNullOrWhiteSpace(accessToken) ||
                    string.IsNullOrWhiteSpace(refreshToken))
                {
                    return Failed("quota.codex_reauth_token_exchange_failed", "Codex token exchange did not return the expected credentials.");
                }

                return new PollResult
                {
                    State = "complete",
                    Tokens = new OAuthTokens
                    {
                        IdToken = idToken,
                        AccessToken = accessToken,
                        RefreshToken = refreshToken
                    }
                };
            }
            catch (OperationCanceledException)
            {
                return Failed("quota.codex_reauth_token_exchange_failed", "Codex token exchange timed out.");
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is JsonException)
            {
                return Failed("quota.codex_reauth_token_exchange_failed", "Codex token exchange failed.");
            }
        }

        private static TcpListener TryBindCallbackListener(out int port)
        {
            foreach (var candidate in new[] { DefaultCallbackPort, FallbackCallbackPort })
            {
                try
                {
                    var listener = new TcpListener(IPAddress.Loopback, candidate);
                    listener.Start();
                    port = candidate;
                    return listener;
                }
                catch (SocketException)
                {
                }
            }
            port = 0;
            return null;
        }

        private static async Task<HttpRequestLine> ReadRequestAsync(TcpClient client, CancellationToken cancellationToken)
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
            var firstLine = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(firstLine)) return null;
            while (true)
            {
                var header = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrEmpty(header)) break;
            }

            var parts = firstLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !string.Equals(parts[0], "GET", StringComparison.OrdinalIgnoreCase)) return null;
            return new HttpRequestLine { Target = parts[1] };
        }

        private static async Task WriteBrowserResponseAsync(TcpClient client, int statusCode, string message, CancellationToken cancellationToken)
        {
            var statusText = statusCode >= 400 ? "Error" : "OK";
            var escaped = WebUtility.HtmlEncode(message ?? string.Empty);
            var body = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>VibeDeck Codex Authorization</title></head><body><main><h1>VibeDeck</h1><p>{escaped}</p></main></body></html>";
            var bytes = Encoding.UTF8.GetBytes(body);
            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {statusCode} {statusText}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
            var stream = client.GetStream();
            await stream.WriteAsync(headers, cancellationToken);
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        private static void CompleteSession(OAuthSession session, PollResult result)
        {
            lock (SessionLock)
            {
                if (!Sessions.ContainsKey(session.SessionId)) return;
                session.Result = result;
                StopListener(session);
            }
        }

        private static void PruneExpiredSessions(DateTimeOffset now)
        {
            foreach (var session in Sessions.Values.Where(item => item.ExpiresAt <= now).ToList())
            {
                Sessions.Remove(session.SessionId);
                StopListener(session);
            }
        }

        private static void StopListener(OAuthSession session)
        {
            try { session.Cancellation?.Cancel(); } catch (ObjectDisposedException) { }
            try { session.Listener?.Stop(); } catch (SocketException) { }
        }

        private static PollResult Failed(string code, string message) => new PollResult
        {
            State = "failed",
            Code = code,
            Message = message
        };

        private static string GenerateBase64UrlToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Base64UrlEncode(bytes);
        }

        private static string Base64UrlEncode(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static bool FixedTimeEquals(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
            var rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
            return leftBytes.Length == rightBytes.Length &&
                CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }

        private static string ReadString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return null;
            return value.GetString();
        }

        private sealed class HttpRequestLine
        {
            public string Target { get; set; }
        }
    }
}
