using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace VibeDeck.Host
{
    public partial class Startup
    {
        // Unauthenticated system/status endpoints: liveness and stream capability
        // discovery. Extracted verbatim from Startup.cs; the capability payload
        // builder WriteStreamCapabilitiesAsync remains on the Startup partial class.
        private static void MapSystemEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/health", async context =>
            {
                context.Response.ContentType = "application/json";
                context.Response.Headers["Cache-Control"] = "no-store";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    status = "ok",
                    app = "VibeDeck.Host",
                    product = AppPaths.ProductName,
                    version = ProductVersion.Current,
                    installed = AppPaths.IsInstalledLayout,
                    transport = "webrtc-h264+jpeg-fallback"
                }));
            });

            endpoints.MapGet("/api/stream/capabilities", async context =>
            {
                context.Response.ContentType = "application/json";
                await WriteStreamCapabilitiesAsync(context);
            });
        }
    }
}
