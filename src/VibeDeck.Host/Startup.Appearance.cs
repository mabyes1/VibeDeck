using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VibeDeck.Host.Appearance;

namespace VibeDeck.Host
{
    public partial class Startup
    {
        private static void MapAppearanceEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/appearance/theme", async context =>
            {
                if (!await RequireTrustedDeviceAsync(context)) return;
                var result = context.RequestServices.GetRequiredService<AppearanceThemeService>().Get();
                await WriteAppearanceJsonAsync(context, result);
            });

            endpoints.MapPut("/api/appearance/theme", async context =>
            {
                if (!await RequireProtectedActionAsync(context)) return;
                try
                {
                    var request = await ReadJsonBodyAsync<AppearanceThemeSettings>(
                        context,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (request == null) return;
                    var result = context.RequestServices.GetRequiredService<AppearanceThemeService>().Save(request);
                    await WriteAppearanceJsonAsync(context, result);
                }
                catch (AppearanceThemeException error)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await WriteAppearanceJsonAsync(context, new { error = error.Message });
                }
            });

            endpoints.MapPost("/api/appearance/background", async context =>
            {
                if (!await RequireProtectedActionAsync(context)) return;
                try
                {
                    var request = await ReadJsonBodyAsync<AppearanceBackgroundRequest>(
                        context,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (request == null) return;
                    var result = context.RequestServices.GetRequiredService<AppearanceThemeService>().SaveBackground(request);
                    await WriteAppearanceJsonAsync(context, result);
                }
                catch (AppearanceThemeException error)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await WriteAppearanceJsonAsync(context, new { error = error.Message });
                }
            });

            endpoints.MapDelete("/api/appearance/background", async context =>
            {
                if (!await RequireProtectedActionAsync(context)) return;
                try
                {
                    var result = context.RequestServices.GetRequiredService<AppearanceThemeService>().ClearBackground();
                    await WriteAppearanceJsonAsync(context, result);
                }
                catch (AppearanceThemeException error)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await WriteAppearanceJsonAsync(context, new { error = error.Message });
                }
            });

            endpoints.MapGet("/api/appearance/background", async context =>
            {
                if (!await RequireTrustedDeviceAsync(context)) return;
                var service = context.RequestServices.GetRequiredService<AppearanceThemeService>();
                if (!service.TryReadBackground(out var bytes, out var version))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                context.Response.ContentType = "image/webp";
                context.Response.Headers["Cache-Control"] = "private, max-age=86400, immutable";
                context.Response.Headers["ETag"] = $"\"{version}\"";
                await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
            });
        }

        private static async Task WriteAppearanceJsonAsync(HttpContext context, object value)
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
