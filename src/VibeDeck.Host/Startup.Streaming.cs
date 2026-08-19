using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VibeDeck.Host.Diagnostics;
using VibeDeck.Host.Security;
using VibeDeck.Host.Streaming;
using VibeDeck.Host.Windows;

namespace VibeDeck.Host
{
    public partial class Startup
    {
        // Live display/input streaming surface: JPEG-over-WebSocket display,
        // WebRTC H.264 signalling, and the input channel. Extracted verbatim from
        // Startup.cs (no behavior change); the runtime loops StreamDisplayAsync /
        // ReceiveInputAsync live in this same file, and ParseInt / SocketJsonOptions
        // remain on the Startup partial class.
        private static void MapStreamingEndpoints(IEndpointRouteBuilder endpoints)
        {
            // Mints a short-lived, single-use ticket for the WebSocket handshake. The device
            // token is presented here as a header (or cookie), which is not written to proxy
            // access logs the way a URL is.
            endpoints.MapPost("/api/stream/ticket", async context =>
            {
                if (!await RequireTrustedDeviceAsync(context))
                {
                    return;
                }

                var devices = context.RequestServices.GetRequiredService<DeviceTrustService>();
                var tickets = context.RequestServices.GetRequiredService<WebSocketTicketService>();
                var ticket = tickets.Issue(
                    devices.ResolveDeviceId(ReadDeviceToken(context)),
                    DateTimeOffset.UtcNow);

                context.Response.ContentType = "application/json";
                context.Response.Headers["Cache-Control"] = "no-store";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    ticket,
                    parameterName = StreamTicketQueryKey,
                    expiresInSeconds = (int)WebSocketTicketService.Lifetime.TotalSeconds
                }), context.RequestAborted);
            });

            endpoints.Map("/ws/display", async context =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                if (!await AuthenticateStreamSocketAsync(context))
                {
                    return;
                }

                var deviceName = context.Request.Query["deviceName"].ToString();
                var fps = ParseInt(context.Request.Query["fps"], 10, 1, 60);
                var quality = ParseInt(context.Request.Query["quality"], 55, 25, 85);
                var frameSource = context.RequestServices.GetRequiredService<DisplayFrameSource>();
                using var session = OpenDeviceSession(context);
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                await StreamDisplayAsync(socket, frameSource, deviceName, fps, quality, session.Token);
            });

            endpoints.MapPost("/api/stream/webrtc/offer", async context =>
            {
                if (!await RequireTrustedDeviceAsync(context))
                {
                    return;
                }

                var request = await ReadJsonBodyAsync<WebRtcOfferRequest>(context, SocketJsonOptions);
                if (request == null) return;
                var webrtc = context.RequestServices.GetRequiredService<WebRtcH264Service>();
                if (!webrtc.IsAvailable)
                {
                    context.Response.StatusCode = StatusCodes.Status501NotImplemented;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        error = "WebRTC H.264 requires ffmpeg.exe on the Host."
                    }));
                    return;
                }

                try
                {
                    var answer = await webrtc.CreateAnswerAsync(
                        request.Sdp,
                        request.DeviceName ?? string.Empty,
                        Math.Max(1, Math.Min(60, request.Fps)),
                        Math.Max(25, Math.Min(85, request.Quality)),
                        context.RequestAborted,
                        context.RequestServices.GetRequiredService<DeviceTrustService>()
                            .ResolveDeviceId(ReadDeviceToken(context)),
                        Math.Max(0, Math.Min(10000, request.ReceiverMaxBitrateKbps)),
                        H264PlayoutDelayExtension.NormalizeMaximumDelayMs(request.PlayoutDelayMs));
                    context.Response.ContentType = "application/json";
                    context.Response.Headers["Cache-Control"] = "no-store";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(answer));
                }
                catch (ArgumentException error)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = error.Message }));
                }
                catch (Exception error)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = error.Message }));
                }
            });

            endpoints.Map("/ws/input", async context =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                if (!await AuthenticateStreamSocketAsync(context))
                {
                    return;
                }

                var devices = context.RequestServices.GetRequiredService<DeviceTrustService>();
                var deviceId = ResolveStreamDeviceId(context);
                using var session = OpenDeviceSession(context, deviceId);
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                var input = context.RequestServices.GetRequiredService<WindowsInputController>();
                await ReceiveInputAsync(
                    socket,
                    input,
                    session.Token,
                    // Belt and braces alongside the registry: if a revocation event were
                    // ever missed, the loop still stops applying input on its own.
                    deviceId == null ? null : () => devices.IsDeviceTrusted(deviceId));
            });
        }

        internal const string StreamTicketQueryKey = "ticket";
        private const string RedeemedDeviceIdItemKey = "VibeDeck.StreamDeviceId";

        /// <summary>
        /// Authenticates a WebSocket handshake, preferring a single-use ticket over the
        /// persistent device token.
        ///
        /// The legacy path (token in the query string) is still accepted so existing clients
        /// keep working, but it is the reason a logged URL used to be a durable credential.
        /// Once the client mints tickets, drop the Query["deviceToken"] branch in
        /// ReadDeviceToken and this fallback with it.
        /// </summary>
        private static async Task<bool> AuthenticateStreamSocketAsync(HttpContext context)
        {
            var ticket = context.Request.Query[StreamTicketQueryKey].ToString();
            if (!string.IsNullOrWhiteSpace(ticket))
            {
                var tickets = context.RequestServices.GetRequiredService<WebSocketTicketService>();
                if (tickets.TryRedeem(ticket, DateTimeOffset.UtcNow, out var deviceId))
                {
                    // Remember the identity the ticket was issued to; the token that proved
                    // it is not on this request.
                    context.Items[RedeemedDeviceIdItemKey] = deviceId;
                    return true;
                }

                // An expired or already-used ticket is a failure in its own right. Falling
                // through to the token path would let a replayed URL work after all.
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    error = "Stream ticket is expired or already used.",
                    code = "ticket_invalid"
                }));
                return false;
            }

            return await RequireTrustedDeviceAsync(context);
        }

        /// <summary>Device behind this stream, whether it authenticated by ticket or token.</summary>
        private static string ResolveStreamDeviceId(HttpContext context)
        {
            if (context.Items.TryGetValue(RedeemedDeviceIdItemKey, out var redeemed))
            {
                return redeemed as string;
            }

            return context.RequestServices
                .GetRequiredService<DeviceTrustService>()
                .ResolveDeviceId(ReadDeviceToken(context));
        }

        /// <summary>
        /// Makes revocation take effect on connections that are already open. Without this
        /// the trust store is updated but the phone keeps streaming and keeps sending input.
        /// </summary>
        private static void WireDeviceRevocationTeardown(IServiceProvider services)
        {
            var devices = services.GetRequiredService<DeviceTrustService>();
            var registry = services.GetRequiredService<DeviceSessionRegistry>();
            var webrtc = services.GetRequiredService<WebRtcH264Service>();
            var audit = services.GetRequiredService<AuditTrailService>();

            devices.DeviceRevoked += deviceId =>
            {
                var sockets = registry.CancelDevice(deviceId);
                var streams = webrtc.CloseSessionsForDevice(deviceId);
                if (sockets > 0 || streams > 0)
                {
                    audit.Record(
                        "warning",
                        "device-trust",
                        "revoke",
                        "live-sessions-terminated",
                        details: new Dictionary<string, string>
                        {
                            ["deviceId"] = deviceId ?? "",
                            ["sockets"] = sockets.ToString(),
                            ["streams"] = streams.ToString()
                        });
                }
            };

            devices.DevicesCleared += () =>
            {
                var sockets = registry.CancelAll();
                var streams = webrtc.CloseAllDeviceSessions();
                if (sockets > 0 || streams > 0)
                {
                    audit.Record(
                        "warning",
                        "device-trust",
                        "clear",
                        "live-sessions-terminated",
                        details: new Dictionary<string, string>
                        {
                            ["sockets"] = sockets.ToString(),
                            ["streams"] = streams.ToString()
                        });
                }
            };
        }

        /// <summary>
        /// Registers this request as a live session for the calling device, so revoking
        /// the device cancels it. The returned token also cancels when the request aborts.
        /// </summary>
        private static DeviceSessionRegistry.DeviceSessionHandle OpenDeviceSession(
            HttpContext context,
            string deviceId = null)
        {
            var registry = context.RequestServices.GetRequiredService<DeviceSessionRegistry>();
            return registry.Open(deviceId ?? ResolveStreamDeviceId(context), context.RequestAborted);
        }

        private static async Task StreamDisplayAsync(
            WebSocket socket,
            DisplayFrameSource frameSource,
            string deviceName,
            int fps,
            int quality,
            CancellationToken cancellationToken)
        {
            var frameInterval = TimeSpan.FromMilliseconds(1000.0 / fps);
            var maxIdleInterval = TimeSpan.FromMilliseconds(Math.Max(450, frameInterval.TotalMilliseconds * 7));
            var keepAliveInterval = TimeSpan.FromSeconds(2.5);
            DisplayFrameFingerprint lastSentFingerprint = null;
            var lastSentAt = DateTimeOffset.MinValue;
            var idleStreak = 0;
            using var holder = new ReusableBitmapHolder();

            try
            {
                while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    var requestedQuality = idleStreak > 0
                        ? Math.Max(38, quality - Math.Min(12, idleStreak * 2))
                        : quality;
                    using var frame = frameSource.CaptureBitmapFrame(deviceName, holder);
                    var changeScore = lastSentFingerprint == null || frame.Fingerprint == null
                        ? 1
                        : frame.Fingerprint.DifferenceFrom(lastSentFingerprint);
                    var hasMeaningfulChange = changeScore >= 0.0125;
                    var shouldSend = frame.IsStatusFrame
                        || hasMeaningfulChange
                        || (DateTimeOffset.UtcNow - lastSentAt) >= keepAliveInterval;

                    if (shouldSend)
                    {
                        var jpegBytes = JpegFrameEncoder.Encode(frame.Bitmap, requestedQuality);
                        await socket.SendAsync(jpegBytes, WebSocketMessageType.Binary, true, cancellationToken);
                        lastSentAt = DateTimeOffset.UtcNow;
                        lastSentFingerprint = frame.Fingerprint;
                        idleStreak = hasMeaningfulChange ? 0 : Math.Min(idleStreak + 1, 8);
                    }
                    else
                    {
                        idleStreak = Math.Min(idleStreak + 1, 8);
                    }

                    var nextDelay = hasMeaningfulChange
                        ? frameInterval
                        : TimeSpan.FromMilliseconds(Math.Min(maxIdleInterval.TotalMilliseconds, frameInterval.TotalMilliseconds * (1.8 + idleStreak * 0.55)));
                    await Task.Delay(nextDelay, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (WebSocketException)
            {
            }
        }

        private static async Task ReceiveInputAsync(
            WebSocket socket,
            WindowsInputController input,
            CancellationToken cancellationToken,
            Func<bool> isStillTrusted = null)
        {
            var buffer = new byte[4096];
            var trustCheckInterval = TimeSpan.FromSeconds(2);
            var nextTrustCheck = DateTimeOffset.UtcNow + trustCheckInterval;

            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                if (isStillTrusted != null && DateTimeOffset.UtcNow >= nextTrustCheck)
                {
                    nextTrustCheck = DateTimeOffset.UtcNow + trustCheckInterval;
                    if (!isStillTrusted())
                    {
                        await CloseRevokedSocketAsync(socket, cancellationToken);
                        return;
                    }
                }

                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested || socket.State != WebSocketState.Open)
                {
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                    {
                        try
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closed", cancellationToken);
                        }
                        catch (Exception) when (cancellationToken.IsCancellationRequested || socket.State != WebSocketState.Open)
                        {
                        }
                    }
                    return;
                }

                var payload = Encoding.UTF8.GetString(buffer, 0, result.Count);
                try
                {
                    var inputEvent = JsonSerializer.Deserialize<InputEvent>(payload, SocketJsonOptions);
                    if (!input.Apply(inputEvent))
                    {
                        Console.WriteLine($"ignored input {inputEvent?.Type} x={inputEvent?.X:0.000} y={inputEvent?.Y:0.000}");
                    }
                }
                catch (JsonException)
                {
                    Console.WriteLine($"invalid input payload: {payload}");
                }
            }
        }

        private static async Task CloseRevokedSocketAsync(WebSocket socket, CancellationToken cancellationToken)
        {
            if (socket.State != WebSocketState.Open)
            {
                return;
            }

            try
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.PolicyViolation,
                    "device revoked",
                    cancellationToken);
            }
            catch (Exception)
            {
                // The peer may already be gone; the caller returns either way.
            }
        }
    }
}
