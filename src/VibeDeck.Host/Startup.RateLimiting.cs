using System;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using VibeDeck.Host.Security;

namespace VibeDeck.Host
{
    public partial class Startup
    {
        // Ingest is the internet-facing custom-source write path. Generous enough for a
        // user's own notification bridge, far below what is needed to saturate SQLite.
        private static readonly RateRule IngestPerClient = new RateRule(burst: 60, perMinute: 120);
        private static readonly RateRule IngestGlobal = new RateRule(burst: 240, perMinute: 600);

        // Pairing creates server-side pending state from an unauthenticated public caller.
        private static readonly RateRule PairingPerClient = new RateRule(burst: 6, perMinute: 12);
        private static readonly RateRule PairingGlobal = new RateRule(burst: 20, perMinute: 60);

        // The per-address lockout in HostAccessAuthService is bypassed by rotating source
        // addresses; this global bucket is what actually bounds a distributed brute force.
        private static readonly RateRule LoginPerClient = new RateRule(burst: 6, perMinute: 12);
        private static readonly RateRule LoginGlobal = new RateRule(burst: 15, perMinute: 40);

        /// <summary>
        /// Throttles the unauthenticated, state-creating endpoints before routing, so a flood
        /// is rejected before the body is read and before any store is opened.
        /// </summary>
        private static void UseRequestRateLimits(IApplicationBuilder app)
        {
            app.Use(async (context, next) =>
            {
                if (!TryResolveRateLimit(context, out var bucket, out var perClient, out var global))
                {
                    await next();
                    return;
                }

                var limiter = context.RequestServices.GetRequiredService<RequestRateLimiter>();
                var now = DateTimeOffset.UtcNow;
                var client = GetRemoteAddress(context);
                if (string.IsNullOrEmpty(client))
                {
                    client = "unknown";
                }

                if (!limiter.TryAcquire($"{bucket}|{client}", perClient, now, out var retryAfter) ||
                    !limiter.TryAcquire($"{bucket}|*", global, now, out retryAfter))
                {
                    await WriteRateLimitedAsync(context, retryAfter);
                    return;
                }

                await next();
            });
        }

        private static bool TryResolveRateLimit(
            HttpContext context,
            out string bucket,
            out RateRule perClient,
            out RateRule global)
        {
            bucket = null;
            perClient = default;
            global = default;

            var path = context.Request.Path.Value ?? string.Empty;
            var method = context.Request.Method;

            if (path.StartsWith("/api/custom-sources/", StringComparison.OrdinalIgnoreCase) &&
                (IsMethod(method, HttpMethods.Post) || IsMethod(method, HttpMethods.Delete)))
            {
                bucket = "custom-source-write";
                perClient = IngestPerClient;
                global = IngestGlobal;
                return true;
            }

            if (IsMethod(method, HttpMethods.Post) &&
                path.Equals("/api/devices/pairing/request", StringComparison.OrdinalIgnoreCase))
            {
                bucket = "pairing-request";
                perClient = PairingPerClient;
                global = PairingGlobal;
                return true;
            }

            if (IsMethod(method, HttpMethods.Post) &&
                path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase))
            {
                bucket = "host-login";
                perClient = LoginPerClient;
                global = LoginGlobal;
                return true;
            }

            return false;
        }

        private static bool IsMethod(string method, string expected)
        {
            return string.Equals(method, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static async System.Threading.Tasks.Task WriteRateLimitedAsync(HttpContext context, double retryAfterSeconds)
        {
            var retryAfter = (int)Math.Max(1, retryAfterSeconds);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/json";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["Retry-After"] = retryAfter.ToString(CultureInfo.InvariantCulture);
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "請求過於頻繁，請稍後再試。",
                code = "rate_limited",
                retryAfterSeconds = retryAfter
            }));
        }
    }
}
