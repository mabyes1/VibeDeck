using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VibeDeck.Host.CustomSources;
using VibeDeck.Host.WindowsNotifications;

namespace VibeDeck.Host
{
    public partial class Startup
    {
        private static void MapWindowsNotificationEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/windows-notifications/status", async context =>
            {
                if (!await RequireLocalRequestAsync(context)) return;
                var service = context.RequestServices.GetRequiredService<WindowsNotificationListenerService>();
                await WriteWindowsNotificationJsonAsync(context, service.GetStatus());
            });

            endpoints.MapPost("/api/windows-notifications/enable", EnableWindowsNotificationsAsync);
            endpoints.MapPost("/api/windows-notifications/disable", DisableWindowsNotificationsAsync);
            endpoints.MapPost("/api/windows-notifications/companion/heartbeat", async context =>
            {
                if (!await RequireLocalRequestAsync(context)) return;
                var service = context.RequestServices.GetRequiredService<WindowsNotificationListenerService>();
                if (!service.ValidateBridgeToken(context.Request.Headers[WindowsNotificationListenerService.BridgeHeaderName]))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                var heartbeat = await ReadJsonBodyAsync<WindowsNotificationCompanionHeartbeat>(
                    context,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (heartbeat == null) return;
                await WriteWindowsNotificationJsonAsync(context, service.AcceptHeartbeat(heartbeat));
            });
            endpoints.MapPost("/api/windows-notifications/companion/events", async context =>
            {
                if (!await RequireLocalRequestAsync(context)) return;
                var service = context.RequestServices.GetRequiredService<WindowsNotificationListenerService>();
                if (!service.ValidateBridgeToken(context.Request.Headers[WindowsNotificationListenerService.BridgeHeaderName]))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                try
                {
                    var notification = await ReadJsonBodyAsync<WindowsNotificationCompanionEvent>(
                        context,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (notification == null) return;
                    await WriteWindowsNotificationJsonAsync(context, service.AcceptEvent(notification));
                }
                catch (ArgumentException error)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await WriteWindowsNotificationJsonAsync(context, new { error = error.Message });
                }
            });
        }

        private static async Task EnableWindowsNotificationsAsync(HttpContext context)
        {
            if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;
            var service = context.RequestServices.GetRequiredService<WindowsNotificationListenerService>();
            await HandleWindowsNotificationAsync(context, () => service.EnableAsync());
        }

        private static async Task DisableWindowsNotificationsAsync(HttpContext context)
        {
            if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;
            var service = context.RequestServices.GetRequiredService<WindowsNotificationListenerService>();
            await HandleWindowsNotificationAsync(context, () => service.DisableAsync());
        }

        private static async Task HandleWindowsNotificationAsync(
            HttpContext context,
            Func<Task<WindowsNotificationStatusResponse>> action)
        {
            try
            {
                await WriteWindowsNotificationJsonAsync(context, await action());
            }
            catch (Exception error)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await WriteWindowsNotificationJsonAsync(context, new
                {
                    error = "windows_notifications_unavailable",
                    message = error.Message
                });
            }
        }

        private static async Task WriteWindowsNotificationJsonAsync(HttpContext context, object value)
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            await context.Response.WriteAsync(JsonSerializer.Serialize(value, CustomSourceJson.Options), context.RequestAborted);
        }
    }
}
