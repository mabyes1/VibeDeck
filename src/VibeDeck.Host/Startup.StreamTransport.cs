using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VibeDeck.Host.Streaming;

namespace VibeDeck.Host
{
    public partial class Startup
    {
        private static void MapStreamTransportEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/stream/ice", async context =>
            {
                if (!await RequireTrustedDeviceAsync(context)) return;
                var service = context.RequestServices.GetRequiredService<CloudflareTurnCredentialService>();
                IceServerConfiguration result;
                try
                {
                    result = await service.CreateIceServersAsync("browser-" + Guid.NewGuid().ToString("N").Substring(0, 12), context.RequestAborted);
                }
                catch (TurnCredentialException error)
                {
                    result = service.GetStunOnlyConfiguration();
                    result.TurnConfigured = true;
                    result.Warning = error.Code;
                    WriteAudit(
                        context,
                        "warning",
                        "stream",
                        "turn-browser-credentials",
                        "fallback-stun",
                        details: new Dictionary<string, string> { ["code"] = error.Code });
                }

                context.Response.ContentType = "application/json";
                context.Response.Headers["Cache-Control"] = "no-store";
                await context.Response.WriteAsync(JsonSerializer.Serialize(result), context.RequestAborted);
            });

            endpoints.MapGet("/api/stream/turn/settings", async context =>
            {
                if (!await RequireLocalRequestAsync(context)) return;
                var settings = context.RequestServices.GetRequiredService<CloudflareTurnCredentialService>().GetSettings();
                await WriteTurnSettingsAsync(context, settings);
            });

            endpoints.MapPost("/api/stream/turn/settings", async context =>
            {
                if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;
                var request = await ReadJsonBodyOrDefaultAsync<TurnSettingsRequest>(context) ?? new TurnSettingsRequest();
                try
                {
                    var service = context.RequestServices.GetRequiredService<CloudflareTurnCredentialService>();
                    service.Configure(request.KeyId, request.ApiToken);
                    WriteAudit(context, "information", "stream", "turn-settings", "saved");
                    await WriteTurnSettingsAsync(context, service.GetSettings());
                }
                catch (TurnSettingsException error)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await WriteTurnErrorAsync(context, error.Code);
                }
            });

            endpoints.MapDelete("/api/stream/turn/settings", async context =>
            {
                if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;
                try
                {
                    var service = context.RequestServices.GetRequiredService<CloudflareTurnCredentialService>();
                    service.ClearSettings();
                    WriteAudit(context, "information", "stream", "turn-settings", "cleared");
                    await WriteTurnSettingsAsync(context, service.GetSettings());
                }
                catch (TurnSettingsException error)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await WriteTurnErrorAsync(context, error.Code);
                }
            });

            endpoints.MapPost("/api/stream/turn/test", async context =>
            {
                if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;
                var service = context.RequestServices.GetRequiredService<CloudflareTurnCredentialService>();
                try
                {
                    var ice = await service.CreateIceServersAsync("pc-test-" + Guid.NewGuid().ToString("N").Substring(0, 12), context.RequestAborted);
                    WriteAudit(context, "information", "stream", "turn-test", "credentials-issued");
                    context.Response.ContentType = "application/json";
                    context.Response.Headers["Cache-Control"] = "no-store";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        success = true,
                        turnConfigured = ice.TurnConfigured,
                        turnAvailable = ice.TurnAvailable,
                        urls = ice.IceServers.SelectMany(server => server.Urls).ToArray()
                    }), context.RequestAborted);
                }
                catch (TurnCredentialException error)
                {
                    WriteAudit(context, "warning", "stream", "turn-test", "failed", details: new Dictionary<string, string> { ["code"] = error.Code });
                    context.Response.StatusCode = StatusCodes.Status502BadGateway;
                    await WriteTurnErrorAsync(context, error.Code);
                }
            });

            endpoints.MapGet("/api/stream/diagnostics", async context =>
            {
                if (!await RequireLocalRequestAsync(context)) return;
                var turn = context.RequestServices.GetRequiredService<CloudflareTurnCredentialService>().GetSettings();
                var webRtc = context.RequestServices.GetRequiredService<WebRtcH264Service>().GetDiagnostics();
                context.Response.ContentType = "application/json";
                context.Response.Headers["Cache-Control"] = "no-store";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    generatedAt = DateTimeOffset.UtcNow.ToString("O"),
                    turnConfigured = turn.IsConfigured,
                    turnKeyId = turn.MaskedKeyId,
                    turnUpdatedAt = turn.UpdatedAt,
                    webRtc
                }), context.RequestAborted);
            });

            endpoints.MapPost("/api/stream/diagnostics", async context =>
            {
                if (!await RequireTrustedDeviceAsync(context)) return;
                var report = await ReadJsonBodyOrDefaultAsync<StreamDiagnosticReport>(context) ?? new StreamDiagnosticReport();
                var severity = string.Equals(report.State, "failed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(report.State, "fallback", StringComparison.OrdinalIgnoreCase)
                    ? "warning"
                    : "information";
                WriteAudit(
                    context,
                    severity,
                    "stream",
                    "client-" + StreamDiagnosticReport.Safe(report.Event, 48),
                    StreamDiagnosticReport.Safe(report.State, 80),
                    details: new Dictionary<string, string>
                    {
                        ["mode"] = StreamDiagnosticReport.Safe(report.Mode, 32),
                        ["path"] = StreamDiagnosticReport.Safe(report.Path, 64),
                        ["connection"] = StreamDiagnosticReport.Safe(report.ConnectionState, 48),
                        ["ice"] = StreamDiagnosticReport.Safe(report.IceState, 48),
                        ["reason"] = StreamDiagnosticReport.Safe(report.Reason, 240)
                    });
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            });
        }

        private static async Task WriteTurnSettingsAsync(HttpContext context, CloudflareTurnSettings settings)
        {
            context.Response.ContentType = "application/json";
            context.Response.Headers["Cache-Control"] = "no-store";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                configured = settings.IsConfigured,
                keyId = settings.MaskedKeyId,
                updatedAt = settings.UpdatedAt
            }), context.RequestAborted);
        }

        private static async Task WriteTurnErrorAsync(HttpContext context, string code)
        {
            context.Response.ContentType = "application/json";
            context.Response.Headers["Cache-Control"] = "no-store";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = code }), context.RequestAborted);
        }
    }

    public sealed class TurnSettingsRequest
    {
        public string KeyId { get; set; }
        public string ApiToken { get; set; }
    }

    public sealed class StreamDiagnosticReport
    {
        public string Event { get; set; }
        public string State { get; set; }
        public string Mode { get; set; }
        public string Path { get; set; }
        public string ConnectionState { get; set; }
        public string IceState { get; set; }
        public string Reason { get; set; }

        internal static string Safe(string value, int limit)
        {
            var normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return normalized.Length <= limit ? normalized : normalized.Substring(0, limit);
        }
    }
}
