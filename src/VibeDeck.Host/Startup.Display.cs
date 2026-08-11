using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VibeDeck.Host.Display;
using VibeDeck.Host.Windows;

namespace VibeDeck.Host
{
    public partial class Startup
    {
        // Display catalog, deck-window, display-mode, and virtual-display endpoints.
        // Extracted verbatim from Startup.cs (VibeDeck 0.1.36) with no behavior change:
        // identical paths, guards (RequireTrustedDevice / RequireProtectedAction /
        // RequireActionToken + RequireLocalRequest), resolved services, request DTOs,
        // status codes, headers, and JSON payloads. Shared helpers (SocketJsonOptions,
        // NormalizeDeckMode, BuildLocalDeckUrl, Require*Async) remain on the Startup
        // partial class and are reused as-is.
        private static void MapDisplayEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/displays", async context =>
            {
                if (!await RequireTrustedDeviceAsync(context))
                {
                    return;
                }

                var catalog = context.RequestServices.GetRequiredService<DisplayCatalog>();
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(catalog.GetDisplays()));
            });

            endpoints.MapPost("/api/deck/launch", async context =>
            {
                if (!await RequireProtectedActionAsync(context))
                {
                    return;
                }

                var request = await ReadJsonBodyAsync<DeckLaunchRequest>(context, SocketJsonOptions);
                if (request == null) return;
                var mode = NormalizeDeckMode(request.Mode);
                var deckUrl = BuildLocalDeckUrl(mode);
                var launcher = context.RequestServices.GetRequiredService<DeckWindowLauncher>();
                var result = launcher.Launch(deckUrl, mode);

                context.Response.StatusCode = result.Success
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(result));
            });

            endpoints.MapPost("/api/deck/return", async context =>
            {
                if (!await RequireProtectedActionAsync(context))
                {
                    return;
                }

                var launcher = context.RequestServices.GetRequiredService<DeckWindowLauncher>();
                var result = launcher.ReturnToPrimary();
                context.Response.StatusCode = result.Success
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status404NotFound;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(result));
            });

            endpoints.MapGet("/api/display/modes", async context =>
            {
                var controller = context.RequestServices.GetRequiredService<DisplayModeController>();
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(controller.GetPresets()));
            });

            endpoints.MapPost("/api/display/mode", async context =>
            {
                if (!await RequireProtectedActionAsync(context))
                {
                    return;
                }

                var controller = context.RequestServices.GetRequiredService<DisplayModeController>();
                var request = await ReadJsonBodyAsync<SetDisplayModeRequest>(context);
                if (request == null) return;
                var result = controller.Apply(request.Width, request.Height, request.RefreshRate);
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(result));
            });

            endpoints.MapGet("/api/display/status", async context =>
            {
                if (!await RequireTrustedDeviceAsync(context))
                {
                    return;
                }

                var controller = context.RequestServices.GetRequiredService<VirtualDisplayController>();
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(controller.GetStatus()));
            });

            endpoints.MapGet("/api/display/install/status", async context =>
            {
                if (!await RequireTrustedDeviceAsync(context))
                {
                    return;
                }

                var installer = context.RequestServices.GetRequiredService<VirtualDisplayInstaller>();
                context.Response.ContentType = "application/json";
                context.Response.Headers["Cache-Control"] = "no-store";
                await context.Response.WriteAsync(JsonSerializer.Serialize(installer.GetStatus()));
            });

            endpoints.MapPost("/api/display/install", async context =>
            {
                if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context))
                {
                    return;
                }

                var installer = context.RequestServices.GetRequiredService<VirtualDisplayInstaller>();
                var status = installer.StartInstall();
                context.Response.StatusCode = status.State == "failed" || status.State == "unavailable"
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status202Accepted;
                context.Response.ContentType = "application/json";
                context.Response.Headers["Cache-Control"] = "no-store";
                await context.Response.WriteAsync(JsonSerializer.Serialize(status));
            });

            endpoints.MapPost("/api/display/enable", async context =>
            {
                if (!await RequireProtectedActionAsync(context))
                {
                    return;
                }

                var controller = context.RequestServices.GetRequiredService<VirtualDisplayController>();
                var request = await ReadJsonBodyAsync<EnableDisplayRequest>(context);
                if (request == null) return;
                var status = controller.Enable(request.Width, request.Height, request.RefreshRate);
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(status));
            });

            endpoints.MapPost("/api/display/disable", async context =>
            {
                if (!await RequireProtectedActionAsync(context))
                {
                    return;
                }

                var controller = context.RequestServices.GetRequiredService<VirtualDisplayController>();
                var status = controller.Disable();
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(status));
            });
        }
    }
}
