using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VibeDeck.Host.Stocks;

namespace VibeDeck.Host
{
    public partial class Startup
    {
        private static void MapStockEndpoints(IEndpointRouteBuilder endpoints)
        {
            static async Task WriteSnapshotAsync(HttpContext context)
            {
                if (!await RequireTrustedDeviceAsync(context)) return;

                var service = context.RequestServices.GetRequiredService<MitakeQuoteService>();
                var marketService = context.RequestServices.GetRequiredService<StockMarketOverviewService>();
                var snapshot = service.GetSnapshot();
                snapshot.Markets = marketService.GetSnapshot();
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.Headers["Cache-Control"] = "no-store";
                await context.Response.WriteAsync(JsonSerializer.Serialize(
                    snapshot,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                    context.RequestAborted);
            }

            endpoints.MapGet("/api/stock-quotes", WriteSnapshotAsync);
            endpoints.MapGet("/api/deck-data/stock-quotes", WriteSnapshotAsync);
            endpoints.MapGet("/api/deck-data/stock-debug", async context =>
            {
                if (!await RequireTrustedDeviceAsync(context)) return;

                var service = context.RequestServices.GetRequiredService<MitakeQuoteService>();
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.Headers["Cache-Control"] = "no-store";
                await context.Response.WriteAsync(JsonSerializer.Serialize(
                    service.GetDebugSnapshot(),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                    context.RequestAborted);
            });
        }
    }
}
