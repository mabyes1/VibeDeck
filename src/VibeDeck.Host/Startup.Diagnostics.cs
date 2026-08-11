using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VibeDeck.Host.Diagnostics;

namespace VibeDeck.Host
{
    public partial class Startup
    {
        private static void MapDiagnosticsEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/diagnostics/audit", async context =>
            {
                if (!await RequireLocalRequestAsync(context)) return;
                var limit = ParseAuditLimit(context.Request.Query["limit"].FirstOrDefault());
                var traceId = context.Request.Query["traceId"].FirstOrDefault();
                var includeRoutine = ParseAuditFlag(context.Request.Query["includeRoutine"].FirstOrDefault());
                var result = context.RequestServices.GetRequiredService<AuditTrailService>().ReadRecent(limit, traceId, includeRoutine);
                context.Response.ContentType = "application/json";
                context.Response.Headers["Cache-Control"] = "no-store";
                await context.Response.WriteAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }), context.RequestAborted);
            });

            endpoints.MapPost("/api/diagnostics/audit/mark", async context =>
            {
                if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;
                var request = await ReadJsonBodyOrDefaultAsync<AuditTrailMarkerRequest>(context)
                    ?? new AuditTrailMarkerRequest();
                WriteAudit(
                    context,
                    "information",
                    "diagnostics",
                    "manual-marker",
                    "recorded",
                    subject: request.Label,
                    details: new Dictionary<string, string>
                    {
                        ["source"] = "pc-console"
                    });
                context.Response.ContentType = "application/json";
                context.Response.Headers["Cache-Control"] = "no-store";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    traceId = GetAuditTraceId(context)
                }), context.RequestAborted);
            });

            endpoints.MapPost("/api/diagnostics/audit/browser-error", async context =>
            {
                if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;
                var request = await ReadJsonBodyOrDefaultAsync<BrowserAuditErrorRequest>(context)
                    ?? new BrowserAuditErrorRequest();
                var kind = string.IsNullOrWhiteSpace(request.Kind) ? "runtime-error" : request.Kind;
                WriteAudit(
                    context,
                    "error",
                    "browser",
                    "client-" + kind,
                    "reported",
                    subject: request.Message,
                    details: new Dictionary<string, string>
                    {
                        ["source"] = request.Source,
                        ["line"] = request.Line.ToString(),
                        ["column"] = request.Column.ToString()
                    });
                context.Response.ContentType = "application/json";
                context.Response.Headers["Cache-Control"] = "no-store";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    traceId = GetAuditTraceId(context)
                }), context.RequestAborted);
            });
        }

        private static int ParseAuditLimit(string value)
        {
            return int.TryParse(value, out var parsed)
                ? Math.Max(1, Math.Min(300, parsed))
                : 80;
        }

        private static bool ParseAuditFlag(string value)
        {
            return value == "1" || bool.TryParse(value, out var parsed) && parsed;
        }

        private static void WriteAudit(
            HttpContext context,
            string severity,
            string category,
            string action,
            string outcome,
            string subject = "",
            IDictionary<string, string> details = null)
        {
            context.RequestServices.GetRequiredService<AuditTrailService>().Record(
                severity,
                category,
                action,
                outcome,
                GetAuditTraceId(context),
                GetRemoteAddress(context),
                subject,
                details);
        }

        private static string GetAuditTraceId(HttpContext context)
        {
            return context.Items.TryGetValue(AuditTrailService.TraceIdItemKey, out var value)
                ? value?.ToString() ?? ""
                : "";
        }

        private static bool ShouldAuditHttpRequest(HttpContext context, long elapsedMilliseconds)
        {
            var path = context.Request.Path.Value ?? "";
            if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) return false;
            if (context.Response.StatusCode >= 400) return true;
            if (IsRoutineTelemetryPath(path)) return elapsedMilliseconds >= 10000;
            if (elapsedMilliseconds >= 1500) return true;
            if (path.StartsWith("/api/diagnostics/audit/", StringComparison.OrdinalIgnoreCase)) return false;
            if (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) ||
                context.Request.Method.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !path.EndsWith("/pairing/poll", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith("/pairing/pending", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRoutineTelemetryPath(string path)
        {
            return path.EndsWith("/windows-notifications/companion/heartbeat", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/sideboard/stats", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/sideboard/work-pulse", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/devices/status", StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class AuditTrailMarkerRequest
    {
        public string Label { get; set; }
    }

    public sealed class BrowserAuditErrorRequest
    {
        public string Kind { get; set; }
        public string Message { get; set; }
        public string Source { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
    }
}
