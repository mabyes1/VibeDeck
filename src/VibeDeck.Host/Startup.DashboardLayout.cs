using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VibeDeck.Host.Dashboard;

namespace VibeDeck.Host
{
    public partial class Startup
    {
        private static void MapDashboardLayoutEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/dashboard/layout", async context =>
            {
                if (!await RequireTrustedDeviceAsync(context)) return;
                var profile = context.Request.Query["profile"].ToString();
                var result = context.RequestServices.GetRequiredService<DashboardLayoutService>().Get(profile);
                await WriteDashboardLayoutJsonAsync(context, result);
            });

            endpoints.MapPut("/api/dashboard/layout", async context =>
            {
                if (!await RequireProtectedActionAsync(context)) return;
                try
                {
                    var request = await ReadJsonBodyAsync<DashboardLayoutUpdateRequest>(
                        context,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (request == null) return;
                    var result = context.RequestServices.GetRequiredService<DashboardLayoutService>().Save(request);
                    WriteAudit(
                        context,
                        "information",
                        "dashboard-layout",
                        "save",
                        "persisted",
                        details: new System.Collections.Generic.Dictionary<string, string>
                        {
                            ["profile"] = result.Profile,
                            ["revision"] = result.Revision.ToString(),
                            ["itemCount"] = result.Items.Count.ToString()
                        });
                    await WriteDashboardLayoutJsonAsync(context, result);
                }
                catch (DashboardLayoutException error)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    WriteAudit(
                        context,
                        "warning",
                        "dashboard-layout",
                        "save",
                        "rejected",
                        details: new System.Collections.Generic.Dictionary<string, string>
                        {
                            ["error"] = error.Message
                        });
                    await WriteDashboardLayoutJsonAsync(context, new { error = error.Message });
                }
            });

            endpoints.MapPost("/api/dashboard/layout/reset", async context =>
            {
                if (!await RequireProtectedActionAsync(context)) return;
                var profile = context.Request.Query["profile"].ToString();
                var result = context.RequestServices.GetRequiredService<DashboardLayoutService>().Reset(profile);
                WriteAudit(
                    context,
                    "warning",
                    "dashboard-layout",
                    "reset",
                    "persisted",
                    details: new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["profile"] = result.Profile,
                        ["revision"] = result.Revision.ToString()
                    });
                await WriteDashboardLayoutJsonAsync(context, result);
            });
        }

        private static async Task WriteDashboardLayoutJsonAsync(HttpContext context, object value)
        {
            context.Response.ContentType = "application/json";
            context.Response.Headers["Cache-Control"] = "no-store";
            await context.Response.WriteAsync(JsonSerializer.Serialize(value, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }), context.RequestAborted);
        }
    }
}
