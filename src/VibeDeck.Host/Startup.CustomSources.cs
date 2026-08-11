using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VibeDeck.Host.Connect;
using VibeDeck.Host.CustomSources;

namespace VibeDeck.Host
{
    public partial class Startup
    {
        private static void MapCustomSourceEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/custom-sources", async context =>
            {
                if (!await RequireLocalRequestAsync(context)) return;
                await HandleCustomAsync(context, async () =>
                {
                    var service = context.RequestServices.GetRequiredService<CustomSourceService>();
                    await WriteCustomJsonAsync(context, new CustomSourcesResponse
                    {
                        Sources = service.GetSources(DateTimeOffset.UtcNow)
                    });
                });
            });

            endpoints.MapPost("/api/custom-sources", async context =>
            {
                if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;
                await HandleCustomAsync(context, async () =>
                {
                    var request = await ReadCustomJsonAsync<CustomSourceCreateRequest>(context);
                    var service = context.RequestServices.GetRequiredService<CustomSourceService>();
                    var urls = BuildCustomSourceEndpointUrls(context, request?.SourceKey);
                    var result = service.Create(request, urls.EndpointUrl, urls.LocalEndpointUrl, DateTimeOffset.UtcNow);
                    context.Response.StatusCode = StatusCodes.Status201Created;
                    await WriteCustomJsonAsync(context, result);
                });
            });

            endpoints.MapMethods("/api/custom-sources/{sourceKey}", new[] { "PATCH" }, async context =>
            {
                if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;
                await HandleCustomAsync(context, async () =>
                {
                    var request = await ReadCustomJsonAsync<CustomSourceUpdateRequest>(context);
                    var service = context.RequestServices.GetRequiredService<CustomSourceService>();
                    await WriteCustomJsonAsync(context, service.Update(
                        context.Request.RouteValues["sourceKey"]?.ToString(),
                        request,
                        DateTimeOffset.UtcNow));
                });
            });

            endpoints.MapPost("/api/custom-sources/{sourceKey}/rotate-token", async context =>
            {
                if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;
                await HandleCustomAsync(context, async () =>
                {
                    var sourceKey = context.Request.RouteValues["sourceKey"]?.ToString();
                    var urls = BuildCustomSourceEndpointUrls(context, sourceKey);
                    var service = context.RequestServices.GetRequiredService<CustomSourceService>();
                    await WriteCustomJsonAsync(context, service.RotateToken(
                        sourceKey,
                        urls.EndpointUrl,
                        urls.LocalEndpointUrl,
                        DateTimeOffset.UtcNow));
                });
            });

            endpoints.MapMethods("/api/custom-sources/{sourceKey}", new[] { "DELETE" }, async context =>
            {
                if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;
                await HandleCustomAsync(context, async () =>
                {
                    var sourceKey = context.Request.RouteValues["sourceKey"]?.ToString();
                    var service = context.RequestServices.GetRequiredService<CustomSourceService>();
                    await WriteCustomJsonAsync(context, service.DeleteSource(sourceKey));
                });
            });

            endpoints.MapGet("/api/custom-cards", async context =>
            {
                if (!await RequireTrustedDeviceAsync(context)) return;
                await HandleCustomAsync(context, async () =>
                {
                    var service = context.RequestServices.GetRequiredService<CustomSourceService>();
                    await WriteCustomJsonAsync(context, service.GetCardSnapshot(DateTimeOffset.UtcNow));
                });
            });

            endpoints.MapGet("/api/custom-cards/{cardId}/settings", async context =>
            {
                if (!await RequireLocalRequestAsync(context)) return;
                await HandleCustomAsync(context, async () =>
                {
                    var service = context.RequestServices.GetRequiredService<CustomSourceService>();
                    await WriteCustomJsonAsync(context, service.GetCardSettings(
                        context.Request.RouteValues["cardId"]?.ToString()));
                });
            });

            endpoints.MapMethods("/api/custom-cards/{cardId}/settings", new[] { "PATCH" }, async context =>
            {
                if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;
                await HandleCustomAsync(context, async () =>
                {
                    var request = await ReadCustomJsonAsync<CustomCardSettingsUpdateRequest>(context);
                    var service = context.RequestServices.GetRequiredService<CustomSourceService>();
                    await WriteCustomJsonAsync(context, service.UpdateCardSettings(
                        context.Request.RouteValues["cardId"]?.ToString(),
                        request,
                        DateTimeOffset.UtcNow));
                });
            });

            endpoints.MapPost("/api/custom-cards/{cardId}/clear", async context =>
            {
                if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;
                await HandleCustomAsync(context, async () =>
                {
                    var service = context.RequestServices.GetRequiredService<CustomSourceService>();
                    await WriteCustomJsonAsync(context, service.ClearCard(
                        context.Request.RouteValues["cardId"]?.ToString(),
                        DateTimeOffset.UtcNow));
                });
            });

            endpoints.MapPost("/api/custom-sources/{sourceKey}/events", async context =>
            {
                var options = context.RequestServices.GetRequiredService<CustomSourceOptions>();
                if (!await IsCustomSourceTransportAllowedAsync(context, options)) return;
                await HandleCustomAsync(context, async () =>
                {
                    EnsureJsonContentType(context);
                    using var document = await ReadCustomDocumentAsync(context, options.MaxPayloadBytes);
                    var service = context.RequestServices.GetRequiredService<CustomSourceService>();
                    var result = service.Ingest(
                        context.Request.RouteValues["sourceKey"]?.ToString(),
                        ReadBearerToken(context),
                        document.RootElement,
                        DateTimeOffset.UtcNow);
                    await WriteCustomJsonAsync(context, result);
                });
            });

            endpoints.MapMethods("/api/custom-sources/{sourceKey}/items/{itemId}", new[] { "DELETE" }, async context =>
            {
                var options = context.RequestServices.GetRequiredService<CustomSourceOptions>();
                if (!await IsCustomSourceTransportAllowedAsync(context, options)) return;
                await HandleCustomAsync(context, async () =>
                {
                    var service = context.RequestServices.GetRequiredService<CustomSourceService>();
                    await WriteCustomJsonAsync(context, service.DeleteItem(
                        context.Request.RouteValues["sourceKey"]?.ToString(),
                        ReadBearerToken(context),
                        context.Request.RouteValues["itemId"]?.ToString(),
                        DateTimeOffset.UtcNow));
                });
            });

            endpoints.MapMethods("/api/custom-sources/{sourceKey}/state", new[] { "DELETE" }, async context =>
            {
                var options = context.RequestServices.GetRequiredService<CustomSourceOptions>();
                if (!await IsCustomSourceTransportAllowedAsync(context, options)) return;
                await HandleCustomAsync(context, async () =>
                {
                    var service = context.RequestServices.GetRequiredService<CustomSourceService>();
                    await WriteCustomJsonAsync(context, service.ClearState(
                        context.Request.RouteValues["sourceKey"]?.ToString(),
                        ReadBearerToken(context),
                        DateTimeOffset.UtcNow));
                });
            });
        }

        private static async Task HandleCustomAsync(HttpContext context, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (CustomSourceProblemException error)
            {
                await WriteCustomErrorAsync(context, error.StatusCode, error.Code, error.Message, error.Fields);
            }
            catch (CustomSourceStoreUnavailableException)
            {
                await WriteCustomErrorAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "custom_sources_unavailable",
                    "Custom source storage is unavailable.");
            }
            catch (JsonException)
            {
                await WriteCustomErrorAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    "invalid_request",
                    "The request body is not valid JSON.");
            }
            catch (InvalidDataException error) when (error.Message == "payload_too_large")
            {
                await WriteCustomErrorAsync(
                    context,
                    StatusCodes.Status413PayloadTooLarge,
                    "payload_too_large",
                    "The request body is too large.");
            }
        }

        private static async Task<T> ReadCustomJsonAsync<T>(HttpContext context)
        {
            EnsureJsonContentType(context);
            var options = context.RequestServices.GetRequiredService<CustomSourceOptions>();
            using var document = await ReadCustomDocumentAsync(context, options.MaxPayloadBytes);
            return document.RootElement.Deserialize<T>(CustomSourceJson.Options);
        }

        private static async Task<JsonDocument> ReadCustomDocumentAsync(HttpContext context, int maxBytes)
        {
            if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > maxBytes)
            {
                throw new InvalidDataException("payload_too_large");
            }

            using var buffer = new MemoryStream();
            var chunk = new byte[Math.Min(8192, maxBytes + 1)];
            while (true)
            {
                var read = await context.Request.Body.ReadAsync(chunk, 0, chunk.Length, context.RequestAborted);
                if (read == 0) break;
                if (buffer.Length + read > maxBytes) throw new InvalidDataException("payload_too_large");
                buffer.Write(chunk, 0, read);
            }

            if (buffer.Length == 0)
            {
                throw new JsonException("Empty JSON body.");
            }
            return JsonDocument.Parse(buffer.ToArray(), CustomSourceJson.DocumentOptions);
        }

        private static void EnsureJsonContentType(HttpContext context)
        {
            var mediaType = (context.Request.ContentType ?? string.Empty).Split(';')[0].Trim();
            if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                throw new CustomSourceProblemException(
                    StatusCodes.Status415UnsupportedMediaType,
                    "unsupported_media_type",
                    "Content-Type must be application/json.");
            }
        }

        private static async Task<bool> IsCustomSourceTransportAllowedAsync(HttpContext context, CustomSourceOptions options)
        {
            if (IsLocalRequest(context) || context.Request.IsHttps || options.AllowInsecureLan) return true;
            await WriteCustomErrorAsync(
                context,
                StatusCodes.Status426UpgradeRequired,
                "upgrade_required",
                "Remote custom-source writes require HTTPS.");
            return false;
        }

        private static string ReadBearerToken(HttpContext context)
        {
            var value = context.Request.Headers["Authorization"].FirstOrDefault() ?? string.Empty;
            const string prefix = "Bearer ";
            return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? value.Substring(prefix.Length).Trim()
                : string.Empty;
        }

        private static (string EndpointUrl, string LocalEndpointUrl) BuildCustomSourceEndpointUrls(HttpContext context, string sourceKey)
        {
            var safeKey = Uri.EscapeDataString((sourceKey ?? string.Empty).Trim().ToLowerInvariant());
            var path = $"api/custom-sources/{safeKey}/events";
            var connect = context.RequestServices.GetRequiredService<ConnectInfoProvider>().Get(context.Request);
            var preferred = new Uri(connect.PreferredUrl.EndsWith("/", StringComparison.Ordinal) ? connect.PreferredUrl : connect.PreferredUrl + "/");
            var endpointUrl = new Uri(preferred, path).ToString();
            var localEndpointUrl = new Uri(new Uri("http://127.0.0.1:5000/"), path).ToString();
            return (endpointUrl, localEndpointUrl);
        }

        private static async Task WriteCustomJsonAsync(HttpContext context, object value)
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            await context.Response.WriteAsync(JsonSerializer.Serialize(value, CustomSourceJson.Options), context.RequestAborted);
        }

        private static async Task WriteCustomErrorAsync(
            HttpContext context,
            int statusCode,
            string code,
            string message,
            System.Collections.Generic.IReadOnlyDictionary<string, string> fields = null)
        {
            if (context.Response.HasStarted) return;
            context.Response.StatusCode = statusCode;
            if (string.Equals(code, "rate_limited", StringComparison.Ordinal) &&
                fields != null &&
                fields.TryGetValue("retryAfterSeconds", out var retryAfter) &&
                int.TryParse(retryAfter, out var retryAfterSeconds))
            {
                context.Response.Headers["Retry-After"] = Math.Max(1, retryAfterSeconds).ToString();
            }
            await WriteCustomJsonAsync(context, new CustomErrorResponse
            {
                Error = new CustomError { Code = code, Message = message, Fields = fields }
            });
        }
    }
}
