using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VibeDeck.Host.Updates;

namespace VibeDeck.Host
{
    public partial class Startup
    {
        private static void MapProductUpdateEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/product-update/status", async context =>
            {
                if (!await RequireLocalRequestAsync(context)) return;
                await WriteProductUpdateJsonAsync(context, context.RequestServices.GetRequiredService<ProductUpdateService>().GetStatus());
            });

            endpoints.MapPost("/api/product-update/check", async context =>
            {
                if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;
                var service = context.RequestServices.GetRequiredService<ProductUpdateService>();
                await WriteProductUpdateJsonAsync(context, await service.CheckAsync(GetAuditTraceId(context)));
            });

            endpoints.MapPost("/api/product-update/start", async context =>
            {
                if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;
                var service = context.RequestServices.GetRequiredService<ProductUpdateService>();
                await WriteProductUpdateJsonAsync(context, service.Start(GetAuditTraceId(context)));
            });
        }

        private static async Task WriteProductUpdateJsonAsync(HttpContext context, ProductUpdateStatus result)
        {
            context.Response.ContentType = "application/json";
            context.Response.Headers["Cache-Control"] = "no-store";
            await context.Response.WriteAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }), context.RequestAborted);
        }
    }
}
