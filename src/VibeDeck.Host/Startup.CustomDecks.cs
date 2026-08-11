using System;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using VibeDeck.Host.CustomDecks;

namespace VibeDeck.Host
{
    public partial class Startup
    {
        private static void UseCustomDeckFiles(IApplicationBuilder app)
        {
            var decks = app.ApplicationServices.GetRequiredService<CustomDeckService>();
            _ = decks.Discover();

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(decks.RootPath),
                RequestPath = "/decks",
                OnPrepareResponse = staticContext =>
                {
                    var response = staticContext.Context.Response;
                    response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
                    response.Headers["Pragma"] = "no-cache";
                    response.Headers["Expires"] = "0";

                    var path = staticContext.Context.Request.Path.Value ?? string.Empty;
                    if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    {
                        // Deck documents are rendered inside VibeDeck's sandboxed iframe.
                        // The iframe deliberately omits allow-same-origin, so Deck JavaScript
                        // cannot read the parent UI or authenticated Host API responses.
                        response.Headers["X-Frame-Options"] = "SAMEORIGIN";
                        response.Headers["Content-Security-Policy"] =
                            "frame-ancestors 'self'; object-src 'none'; base-uri 'self'";
                        response.ContentType = "text/html; charset=utf-8";
                    }
                }
            });
        }

        private static void MapCustomDeckEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/decks", async context =>
            {
                if (!await RequireTrustedDeviceAsync(context)) return;
                var service = context.RequestServices.GetRequiredService<CustomDeckService>();
                context.Response.ContentType = "application/json";
                context.Response.Headers["Cache-Control"] = "no-store";
                await context.Response.WriteAsync(JsonSerializer.Serialize(service.Discover()));
            });

            endpoints.MapPost("/api/decks/open-folder", async context =>
            {
                if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;

                var service = context.RequestServices.GetRequiredService<CustomDeckService>();
                service.EnsureReady();
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{service.RootPath}\"",
                        UseShellExecute = true
                    });
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"ok\":true}");
                }
                catch (Exception ex)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
                }
            });
        }
    }
}
