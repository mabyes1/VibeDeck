using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
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
            var deckFiles = new PhysicalFileProvider(decks.RootPath);

            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value ?? string.Empty;
                var isDeckHtml = (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) &&
                    path.StartsWith("/decks/", StringComparison.OrdinalIgnoreCase) &&
                    path.EndsWith(".html", StringComparison.OrdinalIgnoreCase);
                if (!isDeckHtml)
                {
                    await next();
                    return;
                }

                var relativePath = Uri.UnescapeDataString(path.Substring("/decks/".Length));
                var file = deckFiles.GetFileInfo(relativePath);
                if (!file.Exists || file.IsDirectory)
                {
                    await next();
                    return;
                }

                string html;
                using (var stream = file.CreateReadStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    html = await reader.ReadToEndAsync();
                }
                var bridged = CustomDeckViewerBridge.Inject(html);
                context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
                context.Response.Headers["Pragma"] = "no-cache";
                context.Response.Headers["Expires"] = "0";
                context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
                context.Response.Headers["Content-Security-Policy"] =
                    "frame-ancestors 'self'; object-src 'none'; base-uri 'self'";
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength = Encoding.UTF8.GetByteCount(bridged);
                if (!HttpMethods.IsHead(context.Request.Method))
                    await context.Response.WriteAsync(bridged, Encoding.UTF8, context.RequestAborted);
            });

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = deckFiles,
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
            endpoints.MapMethods("/deck-proxy/{deckId}/{**path}", new[] { "GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS" }, async context =>
            {
                if (!await RequireTrustedDeviceAsync(context)) return;
                var service = context.RequestServices.GetRequiredService<CustomDeckService>();
                var deckId = context.Request.RouteValues["deckId"]?.ToString() ?? string.Empty;
                var deck = service.Find(deckId);
                if (deck == null || !string.Equals(deck.Type, "proxy", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                try
                {
                    var proxy = context.RequestServices.GetRequiredService<CustomDeckProxyService>();
                    await proxy.ProxyAsync(context, deck, context.Request.RouteValues["path"]?.ToString() ?? string.Empty);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is InvalidDataException || ex is WebSocketException)
                {
                    if (!context.Response.HasStarted)
                    {
                        context.Response.StatusCode = StatusCodes.Status502BadGateway;
                        context.Response.ContentType = "text/plain; charset=utf-8";
                        await context.Response.WriteAsync($"Proxy Deck upstream failed: {ex.Message}");
                    }
                }
            });

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
