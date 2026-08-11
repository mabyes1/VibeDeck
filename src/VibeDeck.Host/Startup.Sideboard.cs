using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VibeDeck.Host.Sideboard;

namespace VibeDeck.Host
{
    public partial class Startup
    {
        // GlanceBoard sideboard endpoints (stats / work-pulse / manual refresh).
        // Extracted verbatim from Startup.cs with no behavior change; the shared
        // response writer WriteGlanceBoardResponseAsync remains on the Startup
        // partial class and is reused as-is.
        private static void MapSideboardEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/sideboard/stats", async context =>
            {
                if (!await RequireTrustedDeviceAsync(context))
                {
                    return;
                }

                var proxy = context.RequestServices.GetRequiredService<GlanceBoardProxy>();
                await WriteGlanceBoardResponseAsync(context, await proxy.GetStatsAsync(context.RequestAborted));
            });

            endpoints.MapGet("/api/sideboard/work-pulse", async context =>
            {
                if (!await RequireTrustedDeviceAsync(context))
                {
                    return;
                }

                var proxy = context.RequestServices.GetRequiredService<GlanceBoardProxy>();
                await WriteGlanceBoardResponseAsync(context, await proxy.GetWorkPulseAsync(context.RequestAborted));
            });

            endpoints.MapPost("/api/sideboard/refresh", async context =>
            {
                if (!await RequireProtectedActionAsync(context))
                {
                    return;
                }

                var proxy = context.RequestServices.GetRequiredService<GlanceBoardProxy>();
                await WriteGlanceBoardResponseAsync(context, await proxy.RefreshStatsAsync(context.RequestAborted));
            });
        }
    }
}
