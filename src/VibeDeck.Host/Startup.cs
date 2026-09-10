using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VibeDeck.Host.Connect;
using VibeDeck.Host.CustomDecks;
using VibeDeck.Host.CustomSources;
using VibeDeck.Host.Diagnostics;
using VibeDeck.Host.Display;
using VibeDeck.Host.Dashboard;
using VibeDeck.Host.Quotas;
using VibeDeck.Host.Security;
using VibeDeck.Host.Sideboard;
using VibeDeck.Host.Streaming;
using VibeDeck.Host.Updates;
using VibeDeck.Host.Windows;
using VibeDeck.Host.WindowsNotifications;
using QRCoder;

namespace VibeDeck.Host
{
    public partial class Startup
    {
        private static readonly JsonSerializerOptions SocketJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                    ForwardedHeaders.XForwardedProto |
                    ForwardedHeaders.XForwardedHost;
                options.ForwardLimit = 1;
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
                options.KnownProxies.Add(IPAddress.Loopback);
                options.KnownProxies.Add(IPAddress.IPv6Loopback);
            });
            services.AddSingleton<DisplayCatalog>();
            services.AddSingleton<DisplayFrameSource>();
            services.AddSingleton<H264StreamMetrics>();
            services.AddSingleton<FfmpegGpuCaptureRuntime>();
            services.AddSingleton<IGpuH264CapturePipeline, GpuH264CapturePipeline>();
            services.AddSingleton<H264AnnexBStreamer>();
            services.AddSingleton<CloudflareTurnSettingsStore>();
            services.AddHttpClient<CloudflareTurnCredentialService>(client => client.Timeout = TimeSpan.FromSeconds(12));
            services.AddSingleton<WebRtcH264Service>();
            services.AddSingleton<WindowsInputController>();
            services.AddSingleton<DeckWindowLauncher>();
            services.AddSingleton<DisplayModeController>();
            services.AddSingleton<VirtualDisplayController>();
            services.AddSingleton<VirtualDisplayInstaller>();
            services.AddSingleton<GlanceBoardProxy>();
            services.AddSingleton<AiQuotaService>();
            services.AddSingleton<DashboardEventHub>();
            services.AddSingleton<DashboardLayoutService>();
            services.AddSingleton<CustomDeckService>();
            services.AddSingleton<AuditTrailService>();
            services.AddSingleton<PublicEndpointService>();
            services.AddHttpClient<CloudflareProvisioningClient>(client => client.Timeout = TimeSpan.FromSeconds(25));
            services.AddSingleton<CloudflareConnectorService>();
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<CloudflareConnectorService>());
            services.AddSingleton(sp => new ConnectInfoProvider(
                sp.GetRequiredService<PublicEndpointService>(),
                sp.GetRequiredService<CloudflareConnectorService>()));
            services.AddHttpClient<ConnectionCodeBrokerService>(client => client.Timeout = TimeSpan.FromSeconds(8));
            services.AddHostedService<DashboardChangeMonitor>();
            services.AddSingleton(sp =>
            {
                var options = new CustomSourceOptions();
                sp.GetRequiredService<IConfiguration>().GetSection("CustomSources").Bind(options);
                options.Normalize();
                return options;
            });
            services.AddSingleton<CustomSourceStore>();
            services.AddSingleton<CustomSourceService>();
            services.AddHostedService<CustomSourceCleanupService>();
            services.AddSingleton<WindowsNotificationListenerService>();
            services.AddHostedService(sp => sp.GetRequiredService<WindowsNotificationListenerService>());
            services.AddSingleton<RequestRateLimiter>();
            services.AddSingleton<DeviceSessionRegistry>();
            services.AddSingleton<WebSocketTicketService>();
            services.AddSingleton<ActionTokenService>();
            services.AddSingleton<DeviceTrustService>();
            services.AddSingleton<HostAccessAuthService>();
            services.AddSingleton<ProductUpdateService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            WireDeviceRevocationTeardown(app.ApplicationServices);

            app.ApplicationServices.GetRequiredService<AuditTrailService>().Record(
                "information",
                "host",
                "startup",
                "ready",
                details: new Dictionary<string, string>
                {
                    ["version"] = ProductVersion.Current,
                    ["installed"] = AppPaths.IsInstalledLayout ? "true" : "false",
                    ["dataRoot"] = AppPaths.DataRoot
                });

            // Only a local connector may supply X-Forwarded-* data. Preserve the
            // socket peer before Forwarded Headers replaces it with the phone IP.
            app.Use(async (context, next) =>
            {
                context.Items[PublicEndpointService.OriginalRemoteAddressItemKey] = context.Connection.RemoteIpAddress;
                context.Items[OriginalHostItemKey] = context.Request.Host.Value;
                await next();
            });
            app.UseForwardedHeaders();

            app.Use(async (context, next) =>
            {
                var audit = context.RequestServices.GetRequiredService<AuditTrailService>();
                var traceId = AuditTrailService.CreateTraceId(context.Request.Headers["X-VibeDeck-Trace-Id"].FirstOrDefault());
                context.Items[AuditTrailService.TraceIdItemKey] = traceId;
                context.Response.Headers["X-VibeDeck-Trace-Id"] = traceId;
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await next();
                }
                catch (Exception error)
                {
                    audit.RecordException(
                        "http",
                        $"{context.Request.Method} {context.Request.Path}",
                        error,
                        traceId,
                        GetRemoteAddress(context),
                        details: new Dictionary<string, string>
                        {
                            ["method"] = context.Request.Method,
                            ["path"] = context.Request.Path.Value ?? "",
                            ["local"] = IsLocalRequest(context) ? "true" : "false"
                        });
                    throw;
                }
                finally
                {
                    stopwatch.Stop();
                    if (ShouldAuditHttpRequest(context, stopwatch.ElapsedMilliseconds))
                    {
                        audit.Record(
                            context.Response.StatusCode >= 400 ? "warning" : "information",
                            "http",
                            $"{context.Request.Method} {context.Request.Path}",
                            context.Response.StatusCode >= 400 ? "failed" : "completed",
                            traceId,
                            GetRemoteAddress(context),
                            details: new Dictionary<string, string>
                            {
                                ["status"] = context.Response.StatusCode.ToString(),
                                ["durationMs"] = stopwatch.ElapsedMilliseconds.ToString(),
                                ["local"] = IsLocalRequest(context) ? "true" : "false"
                            });
                    }
                }
            });

            app.Use(async (context, next) =>
            {
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["X-Frame-Options"] = "DENY";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'; object-src 'none'; base-uri 'self'";
                await next();
            });

            // Ahead of routing, body reads and store access: an unauthenticated flood must be
            // rejected before it can cost a JSON parse or a SQLite open.
            UseRequestRateLimits(app);

            app.UseRouting();
            app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(15)
            });
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value ?? string.Empty;
                if (path.StartsWith("/device-lab", StringComparison.OrdinalIgnoreCase) &&
                    !IsLocalRequest(context))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                await next();
            });
            UseCustomDeckFiles(app);
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = staticContext =>
                {
                    var path = staticContext.Context.Request.Path.Value ?? string.Empty;
                    var devicePreview = path.Equals("/index.html", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(staticContext.Context.Request.Query["devicePreview"].ToString()) &&
                        IsLocalRequest(staticContext.Context);
                    if (devicePreview)
                    {
                        staticContext.Context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
                        staticContext.Context.Response.Headers["Content-Security-Policy"] =
                            "frame-ancestors 'self'; object-src 'none'; base-uri 'self'";
                    }

                    if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    {
                        staticContext.Context.Response.ContentType = "text/html; charset=utf-8";
                    }
                    else if (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                    {
                        staticContext.Context.Response.ContentType = "text/javascript; charset=utf-8";
                    }
                    else if (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                    {
                        staticContext.Context.Response.ContentType = "text/css; charset=utf-8";
                    }
                    else if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        staticContext.Context.Response.ContentType = "application/json; charset=utf-8";
                    }

                    if (path.Equals("/index.html", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/service-worker.js", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        staticContext.Context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
                        staticContext.Context.Response.Headers["Pragma"] = "no-cache";
                        staticContext.Context.Response.Headers["Expires"] = "0";
                    }
                }
            });

            app.UseEndpoints(endpoints =>
            {
                MapCustomSourceEndpoints(endpoints);
                MapCustomDeckEndpoints(endpoints);
                MapDashboardLayoutEndpoints(endpoints);
                MapDiagnosticsEndpoints(endpoints);
                MapStreamTransportEndpoints(endpoints);
                MapProductUpdateEndpoints(endpoints);
                MapWindowsNotificationEndpoints(endpoints);
                MapDisplayEndpoints(endpoints);
                MapSystemEndpoints(endpoints);
                MapOnboardingAssetEndpoints(endpoints);
                MapSideboardEndpoints(endpoints);
                MapQuotaEndpoints(endpoints);
                MapStreamingEndpoints(endpoints);

                endpoints.MapGet("/api/auth/status", async context =>
                {
                    var auth = context.RequestServices.GetRequiredService<HostAccessAuthService>();
                    var local = IsLocalRequest(context);
                    context.Response.ContentType = "application/json";
                    context.Response.Headers["Cache-Control"] = "no-store";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        enabled = auth.Enabled,
                        authenticated = local || auth.IsAuthenticated(context),
                        required = auth.Enabled && !local,
                        httpsRequired = auth.Enabled && !local && !context.Request.IsHttps
                    }));
                });

                endpoints.MapPost("/api/auth/login", async context =>
                {
                    var auth = context.RequestServices.GetRequiredService<HostAccessAuthService>();
                    var local = IsLocalRequest(context);
                    var request = await ReadJsonBodyAsync<HostLoginRequest>(context, SocketJsonOptions);
                    if (request == null) return;

                    if (auth.Enabled && !local && !context.Request.IsHttps)
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "遠端登入必須使用 HTTPS。", code = "auth.https_required" }));
                        return;
                    }

                    var result = auth.Login(request.Password, GetRemoteAddress(context));
                    if (result.Success)
                    {
                        var sessionCookie = new CookieOptions
                        {
                            HttpOnly = true,
                            Secure = context.Request.IsHttps,
                            SameSite = SameSiteMode.Lax,
                            Path = "/",
                            MaxAge = result.SessionLifetime,
                            IsEssential = true
                        };
                        context.Response.Cookies.Append(HostAccessAuthService.CookieName, result.SessionToken, sessionCookie);
                    }

                    context.Response.StatusCode = result.Success
                        ? StatusCodes.Status200OK
                        : StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        success = result.Success,
                        code = result.Code,
                        message = result.Message
                    }));
                });

                endpoints.MapPost("/api/auth/logout", async context =>
                {
                    context.RequestServices.GetRequiredService<HostAccessAuthService>().Logout(context);
                    var clearCookie = new CookieOptions { Path = "/", Secure = context.Request.IsHttps };
                    context.Response.Cookies.Delete(HostAccessAuthService.CookieName, clearCookie);
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = true }));
                });

                endpoints.MapGet("/api/connect", async context =>
                {
                    var provider = context.RequestServices.GetRequiredService<ConnectInfoProvider>();
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(provider.Get(context)));
                });

                endpoints.MapPost("/api/connect/public-endpoint", async context =>
                {
                    if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context))
                    {
                        return;
                    }

                    var request = await ReadJsonBodyOrDefaultAsync<PublicEndpointRequest>(context)
                        ?? new PublicEndpointRequest();
                    var endpointsService = context.RequestServices.GetRequiredService<PublicEndpointService>();
                    try
                    {
                        var configured = endpointsService.Configure(request.PublicUrl);
                        WriteAudit(
                            context,
                            "information",
                            "public-endpoint",
                            "configure",
                            "completed",
                            configured.InstallationId,
                            new Dictionary<string, string>
                            {
                                ["host"] = $"{configured.InstallationId}.{configured.BaseDomain}"
                            });
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(configured));
                    }
                    catch (PublicEndpointException error)
                    {
                        WriteAudit(
                            context,
                            "warning",
                            "public-endpoint",
                            "configure",
                            "rejected",
                            details: new Dictionary<string, string>
                            {
                                ["message"] = error.Message
                            });
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = error.Message, code = error.Code }));
                    }
                });

                RequestDelegate issueConnectionCode = async context =>
                {
                    if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context))
                    {
                        return;
                    }

                    var endpoint = context.RequestServices.GetRequiredService<PublicEndpointService>().GetConfiguration();
                    var broker = context.RequestServices.GetRequiredService<ConnectionCodeBrokerService>();
                    var result = await broker.IssueAsync(endpoint, context.RequestAborted);
                    WriteAudit(
                        context,
                        result.IsSuccess ? "information" : "warning",
                        "connection-code",
                        "issue",
                        result.IsSuccess ? "completed" : "failed",
                        details: new Dictionary<string, string>
                        {
                            ["broker"] = result.BrokerUrl ?? "",
                            ["errorCode"] = result.ErrorCode ?? "",
                            ["expiresAt"] = result.ExpiresAt == default ? "" : result.ExpiresAt.ToString("O")
                        });
                    context.Response.StatusCode = result.IsSuccess
                        ? StatusCodes.Status200OK
                        : result.ErrorCode == "connection_code.secure_url_required"
                            ? StatusCodes.Status409Conflict
                            : StatusCodes.Status503ServiceUnavailable;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        success = result.IsSuccess,
                        code = result.Code,
                        brokerUrl = result.BrokerUrl,
                        expiresAt = result.ExpiresAt == default ? "" : result.ExpiresAt.ToString("O"),
                        error = result.IsSuccess ? null : new { code = result.ErrorCode }
                    }));
                };
                endpoints.MapPost("/api/connect/device-code", issueConnectionCode);
                endpoints.MapPost("/api/connect/eink-code", issueConnectionCode);

                endpoints.MapDelete("/api/connect/public-endpoint", async context =>
                {
                    if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context))
                    {
                        return;
                    }

                    var endpointsService = context.RequestServices.GetRequiredService<PublicEndpointService>();
                    try
                    {
                        var cleared = endpointsService.Clear();
                        WriteAudit(
                            context,
                            "warning",
                            "public-endpoint",
                            "clear",
                            "completed",
                            cleared.InstallationId);
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(cleared));
                    }
                    catch (PublicEndpointException error)
                    {
                        WriteAudit(
                            context,
                            "error",
                            "public-endpoint",
                            "clear",
                            "failed",
                            details: new Dictionary<string, string>
                            {
                                ["message"] = error.Message
                            });
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = error.Message, code = error.Code }));
                    }
                });

                endpoints.MapGet("/api/session", async context =>
                {
                    if (!IsTrustedLocalConsole(context) &&
                        !context.RequestServices.GetRequiredService<HostAccessAuthService>().IsAuthenticated(context) &&
                        !context.RequestServices.GetRequiredService<DeviceTrustService>().IsTrusted(
                            ReadDeviceToken(context),
                            GetRemoteAddress(context),
                            context.Request.Headers["User-Agent"].FirstOrDefault()))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Remote login required." }));
                        return;
                    }

                    var token = context.RequestServices.GetRequiredService<ActionTokenService>();
                    context.Response.ContentType = "application/json";
                    context.Response.Headers["Cache-Control"] = "no-store";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        actionToken = token.Token,
                        actionHeader = ActionTokenService.HeaderName,
                        deviceHeader = DeviceTrustService.HeaderName,
                        product = AppPaths.ProductName,
                        version = ProductVersion.Current,
                        installed = AppPaths.IsInstalledLayout
                    }));
                });

                endpoints.MapGet("/api/devices/status", async context =>
                {
                    var devices = context.RequestServices.GetRequiredService<DeviceTrustService>();
                    var isLocal = IsLocalRequest(context);
                    var hostAuthenticated = context.RequestServices.GetRequiredService<HostAccessAuthService>().IsAuthenticated(context);
                    context.Response.ContentType = "application/json";
                    context.Response.Headers["Cache-Control"] = "no-store";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(devices.GetStatus(
                        ReadDeviceToken(context),
                        GetRemoteAddress(context),
                        context.Request.Headers["User-Agent"].FirstOrDefault(),
                        isLocal,
                        hostAuthenticated,
                        Uri.UnescapeDataString(context.Request.Headers[DeviceTrustService.DeviceModelHeaderName].FirstOrDefault() ?? ""),
                        context.Request.Headers[DeviceTrustService.ClientInstanceHeaderName].FirstOrDefault())));
                });

                endpoints.MapPost("/api/devices/revoke", async context =>
                {
                    if (!await RequireProtectedActionAsync(context))
                    {
                        return;
                    }

                    var request = await ReadJsonBodyAsync<DeviceRevokeRequest>(context, SocketJsonOptions);
                    if (request == null) return;
                    var devices = context.RequestServices.GetRequiredService<DeviceTrustService>();
                    var result = devices.RevokeDevice(request.DeviceId);
                    WriteAudit(
                        context,
                        result.Success ? "information" : "warning",
                        "pairing",
                        "revoke",
                        result.Success ? "completed" : "not-found",
                        details: new Dictionary<string, string>
                        {
                            ["message"] = result.Message
                        });
                    context.Response.StatusCode = result.Success
                        ? StatusCodes.Status200OK
                        : StatusCodes.Status404NotFound;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(result));
                });

                endpoints.MapPost("/api/devices/clear", async context =>
                {
                    if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context))
                    {
                        return;
                    }

                    var devices = context.RequestServices.GetRequiredService<DeviceTrustService>();
                    var result = devices.ClearDevices();
                    WriteAudit(
                        context,
                        result.Success ? "warning" : "error",
                        "pairing",
                        "clear-all",
                        result.Success ? "completed" : "failed",
                        details: new Dictionary<string, string>
                        {
                            ["message"] = result.Message
                        });
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(result));
                });

                endpoints.MapPost("/api/devices/pairing/request", async context =>
                {
                    if (!await RequirePairingTransportAsync(context)) return;
                    var request = await ReadJsonBodyOrDefaultAsync<PairingApprovalStartRequest>(context)
                        ?? new PairingApprovalStartRequest();
                    var devices = context.RequestServices.GetRequiredService<DeviceTrustService>();
                    var result = devices.RequestApproval(
                        request.Name,
                        request.Platform,
                        request.Model,
                        request.ClientInstanceId,
                        context.Request.Headers["User-Agent"].FirstOrDefault(),
                        GetRemoteAddress(context));
                    WriteAudit(
                        context,
                        "information",
                        "pairing",
                        "request",
                        "pending",
                        request.Name,
                        new Dictionary<string, string>
                        {
                            ["platform"] = request.Platform,
                            ["model"] = request.Model
                        });
                    context.Response.ContentType = "application/json";
                    context.Response.Headers["Cache-Control"] = "no-store";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(result));
                });

                endpoints.MapPost("/api/devices/pairing/poll", async context =>
                {
                    if (!await RequirePairingTransportAsync(context)) return;
                    var request = await ReadJsonBodyOrDefaultAsync<PairingApprovalPollRequest>(context)
                        ?? new PairingApprovalPollRequest();
                    var devices = context.RequestServices.GetRequiredService<DeviceTrustService>();
                    var result = devices.PollApproval(request.RequestId, request.RequestSecret);
                    if (result.Success && result.Status == "approved" && !string.IsNullOrWhiteSpace(result.DeviceToken))
                    {
                        var deviceCookie = new CookieOptions
                        {
                            HttpOnly = false, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Lax,
                            Path = "/", MaxAge = TimeSpan.FromDays(400), IsEssential = true
                        };
                        context.Response.Cookies.Append(DeviceTrustService.CookieName, result.DeviceToken, deviceCookie);
                    }
                    if (!result.Success || result.Status == "approved" || result.Status == "denied")
                    {
                        WriteAudit(
                            context,
                            result.Success ? "information" : "warning",
                            "pairing",
                            "poll",
                            result.Status ?? "unknown",
                            result.DeviceName,
                            new Dictionary<string, string>
                            {
                                ["continued"] = result.Continued ? "true" : "false",
                                ["message"] = result.Message
                            });
                    }
                    context.Response.StatusCode = result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest;
                    context.Response.ContentType = "application/json";
                    context.Response.Headers["Cache-Control"] = "no-store";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(result));
                });

                endpoints.MapPost("/api/devices/pairing/pending", async context =>
                {
                    if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;
                    var devices = context.RequestServices.GetRequiredService<DeviceTrustService>();
                    context.Response.ContentType = "application/json";
                    context.Response.Headers["Cache-Control"] = "no-store";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { requests = devices.GetPendingApprovals() }));
                });

                endpoints.MapPost("/api/devices/pairing/approve", async context =>
                {
                    if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;
                    var request = await ReadJsonBodyOrDefaultAsync<PairingApprovalActionRequest>(context)
                        ?? new PairingApprovalActionRequest();
                    var result = context.RequestServices.GetRequiredService<DeviceTrustService>().ApproveRequest(request.RequestId);
                    WriteAudit(
                        context,
                        result.Success ? "information" : "warning",
                        "pairing",
                        "approve",
                        result.Success ? "approved" : "not-found",
                        details: new Dictionary<string, string>
                        {
                            ["message"] = result.Message
                        });
                    context.Response.StatusCode = result.Success ? StatusCodes.Status200OK : StatusCodes.Status404NotFound;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(result));
                });

                endpoints.MapPost("/api/devices/pairing/deny", async context =>
                {
                    if (!await RequireActionTokenAsync(context) || !await RequireLocalRequestAsync(context)) return;
                    var request = await ReadJsonBodyOrDefaultAsync<PairingApprovalActionRequest>(context)
                        ?? new PairingApprovalActionRequest();
                    var result = context.RequestServices.GetRequiredService<DeviceTrustService>().DenyRequest(request.RequestId);
                    WriteAudit(
                        context,
                        result.Success ? "information" : "warning",
                        "pairing",
                        "deny",
                        result.Success ? "denied" : "not-found",
                        details: new Dictionary<string, string>
                        {
                            ["message"] = result.Message
                        });
                    context.Response.StatusCode = result.Success ? StatusCodes.Status200OK : StatusCodes.Status404NotFound;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(result));
                });

                endpoints.MapGet("/api/dashboard/events", async context =>
                {
                    if (!await RequireTrustedDeviceAsync(context))
                    {
                        return;
                    }

                    var hub = context.RequestServices.GetRequiredService<DashboardEventHub>();
                    context.Response.ContentType = "text/event-stream";
                    context.Response.Headers["Cache-Control"] = "no-cache, no-transform";
                    context.Response.Headers["X-Accel-Buffering"] = "no";
                    using var subscription = hub.Subscribe();
                    try
                    {
                        await context.Response.WriteAsync("retry: 3000\nevent: sync\ndata: initial\n\n", context.RequestAborted);
                        await context.Response.Body.FlushAsync(context.RequestAborted);

                        while (!context.RequestAborted.IsCancellationRequested)
                        {
                            var notification = await subscription.Reader.ReadAsync(context.RequestAborted);
                            var data = notification.DataJson ?? DateTimeOffset.UtcNow.ToString("O");
                            await context.Response.WriteAsync($"event: {notification.Topic}\ndata: {data}\n\n", context.RequestAborted);
                            await context.Response.Body.FlushAsync(context.RequestAborted);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Clean disconnect
                    }
                });

                endpoints.MapGet("/", async context =>
                {
                    if (IsAgyOAuthCallbackRequest(context.Request))
                    {
                        await WriteAgyOAuthCallbackAsync(context);
                        return;
                    }

                    // Serve the entry page directly. Safari rejects redirected navigation
                    // responses that were previously intercepted by an installed service
                    // worker, and the resulting stale shell can return HTML to JSON APIs.
                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
                    await context.Response.SendFileAsync(Path.Combine(env.WebRootPath, "index.html"));
                });
            });
        }

        private static Task WriteStreamCapabilitiesAsync(HttpContext context)
        {
            var h264 = context.RequestServices.GetRequiredService<H264AnnexBStreamer>();
            var h264Metrics = context.RequestServices.GetRequiredService<H264StreamMetrics>();
            var webrtc = context.RequestServices.GetRequiredService<WebRtcH264Service>();
            return context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                jpeg = new
                {
                    supported = true,
                    transport = "websocket",
                    path = "/ws/display",
                    notes = "Current reliable display stream. Tunable by fps and quality."
                },
                h264 = new
                {
                    supported = h264.IsAvailable,
                    transport = "webrtc-h264",
                    path = "/api/stream/webrtc/offer",
                    encoder = h264.IsAvailable ? h264.EncoderDescription : null,
                    codecProfile = H264WebRtcCodecContract.AdvertisedProfile,
                    codecParameters = H264WebRtcCodecContract.SdpFormatParameters,
                    gpuCapture = h264.IsAvailable && h264.IsGpuCaptureAvailable,
                    capturePath = h264.IsAvailable && h264.IsGpuCaptureAvailable
                        ? "D3D11 GPU direct; bitmap fallback"
                        : "bitmap fallback",
                    clientDecoder = "Browser WebRTC H.264",
                    missing = h264.IsAvailable ? null : "ffmpeg.exe was not found.",
                    next = h264.IsAvailable
                        ? "WebRTC prefers D3D11 GPU capture and hardware H.264, automatically lowers its quality tier when encoding falls behind, and retains a CPU-safe bitmap fallback."
                        : "Install FFmpeg or set VIBEDECK_FFMPEG to an ffmpeg.exe path.",
                    metrics = h264Metrics.GetSnapshot()
                },
                webrtc = new
                {
                    supported = webrtc.IsAvailable,
                    signalling = "/api/stream/webrtc/offer",
                    transport = "webrtc-h264",
                    next = webrtc.IsAvailable
                        ? "WebRTC uses direct STUN candidates first, optional Cloudflare TURN relay, and a stable JPEG fallback."
                        : "Install FFmpeg or set VIBEDECK_FFMPEG to an ffmpeg.exe path."
                }
            }));
        }

        private static async Task WriteGlanceBoardResponseAsync(HttpContext context, GlanceBoardResponse response)
        {
            context.Response.ContentType = "application/json";
            if (response.IsAvailable)
            {
                await context.Response.WriteAsync(response.Json ?? "{}");
                return;
            }

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                isAvailable = false,
                error = response.Error,
                upstream = response.Json
            }));
        }

        private static async Task WriteQrSvgAsync(HttpContext context, string value)
        {
            context.Response.ContentType = "image/svg+xml";
            context.Response.Headers["Cache-Control"] = "no-store";
            await context.Response.WriteAsync(BuildQrSvg(value));
        }

        private static string BuildQrSvg(string value)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.Q);
            var qr = new SvgQRCode(data);
            return qr.GetGraphic(4);
        }

        private static async Task WriteCertificateFileAsync(HttpContext context, string path, string fileName, string contentType)
        {
            if (!File.Exists(path))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    error = "VibeDeck HTTPS certificate is not configured. Reinstall or restart the Host so it can mint a local certificate."
                }));
                return;
            }

            context.Response.ContentType = contentType;
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
            await context.Response.SendFileAsync(path);
        }

        private static int ParseInt(string value, int defaultValue, int min, int max)
        {
            if (!int.TryParse(value, out var parsed))
            {
                return defaultValue;
            }

            return Math.Max(min, Math.Min(max, parsed));
        }

        /// <summary>
        /// Read JSON body without crashing Kestrel on empty/malformed payloads
        /// (e.g. PowerShell backtick mangling: {"name":`}).
        /// </summary>
        private static async Task<T> ReadJsonBodyOrDefaultAsync<T>(HttpContext context) where T : class, new()
        {
            try
            {
                if (context.Request.ContentLength == 0)
                {
                    return new T();
                }

                // Allow re-read if a previous middleware already consumed the body.
                context.Request.EnableBuffering();
                if (context.Request.Body.CanSeek)
                {
                    context.Request.Body.Position = 0;
                }

                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
                var text = await reader.ReadToEndAsync();
                if (context.Request.Body.CanSeek)
                {
                    context.Request.Body.Position = 0;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    return new T();
                }

                text = text.Trim().TrimStart('\uFEFF');
                try
                {
                    return JsonSerializer.Deserialize<T>(text, SocketJsonOptions) ?? new T();
                }
                catch (JsonException)
                {
                    // Malformed body (empty, HTML, PowerShell backtick mangling, etc.) → defaults.
                    return new T();
                }
            }
            catch (JsonException)
            {
                return new T();
            }
            catch (IOException)
            {
                return new T();
            }
        }

        /// <summary>
        /// Reads a JSON request body, answering 400 rather than throwing when it is malformed.
        /// Returns null when the body was rejected, in which case the response is already
        /// written and the caller must return.
        ///
        /// Deserializing straight into the handler turns any junk body into an unhandled
        /// exception and a bare 500 - including on /api/auth/login, which is reachable from
        /// the internet without credentials.
        /// </summary>
        private static async Task<T> ReadJsonBodyAsync<T>(HttpContext context, JsonSerializerOptions options = null)
            where T : class, new()
        {
            try
            {
                return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, options) ?? new T();
            }
            catch (JsonException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    error = "Request body is not valid JSON.",
                    code = "invalid_json"
                }));
                return null;
            }
        }

        private static async Task<bool> RequireActionTokenAsync(HttpContext context)
        {
            var tokens = context.RequestServices.GetRequiredService<ActionTokenService>();
            var supplied = context.Request.Headers[ActionTokenService.HeaderName].FirstOrDefault();

            if (tokens.IsValid(supplied))
            {
                return true;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "VibeDeck action token is missing or invalid."
            }));
            return false;
        }

        private static async Task<bool> RequireProtectedActionAsync(HttpContext context)
        {
            return await RequireActionTokenAsync(context) &&
                await RequireTrustedDeviceAsync(context);
        }

        private static async Task<bool> RequireLocalRequestAsync(HttpContext context)
        {
            if (IsTrustedLocalConsole(context))
            {
                return true;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "This action can only be started from this PC."
            }));
            return false;
        }

        private static async Task<bool> RequirePairingTransportAsync(HttpContext context)
        {
            if (IsPrivateLanRequest(context) ||
                context.RequestServices.GetRequiredService<PublicEndpointService>().IsTrustedPublicRequest(context))
            {
                return await RequireHttpsPairingAsync(context);
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "Pairing is only available on the local network or through this PC's configured VibeDeck secure URL."
            }));
            return false;
        }

        private static bool IsPrivateLanRequest(HttpContext context)
        {
            var address = context.Connection.RemoteIpAddress;
            if (address != null && address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            return address != null &&
                (IPAddress.IsLoopback(address) || IsLocalMachineAddress(address) || IsPrivateIpv4(address));
        }

        private static async Task<bool> RequireHttpsPairingAsync(HttpContext context)
        {
            if (context.Request.IsHttps)
            {
                return true;
            }

            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "手機配對必須使用 HTTPS。請掃描 PC 顯示的 QR Code；若瀏覽器顯示警告，請按「進階」並繼續前往。"
            }));
            return false;
        }

        private static bool IsPrivateIpv4(IPAddress address)
        {
            if (address.AddressFamily != AddressFamily.InterNetwork) return false;
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168) ||
                // Tailscale IPv4 uses the RFC 6598 shared address range.
                (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127);
        }

        private static async Task<bool> RequireTrustedDeviceAsync(HttpContext context)
        {
            // Local console = local peer + expected Host + not cross-site. A loopback
            // socket alone is not enough: a hostile page can open ws://127.0.0.1:5000/ws/*
            // or call local read APIs from the same machine. /api/session already uses
            // IsTrustedLocalConsole; this gate must match it for /ws/display, /ws/input,
            // and every other RequireTrustedDevice path.
            if (IsTrustedLocalConsole(context))
            {
                return true;
            }

            var hostAuth = context.RequestServices.GetRequiredService<HostAccessAuthService>();
            if (hostAuth.IsAuthenticated(context))
            {
                return true;
            }

            var devices = context.RequestServices.GetRequiredService<DeviceTrustService>();
            if (devices.IsTrusted(
                ReadDeviceToken(context),
                GetRemoteAddress(context),
                context.Request.Headers["User-Agent"].FirstOrDefault()))
            {
                return true;
            }

            context.Response.StatusCode = hostAuth.Enabled
                ? StatusCodes.Status401Unauthorized
                : StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = hostAuth.Enabled ? "Remote login required." : "Phone is not paired with this Host."
            }));
            return false;
        }

        private static bool IsLocalRequest(HttpContext context)
        {
            var address = context.Connection.RemoteIpAddress;
            if (address == null)
            {
                return false;
            }

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            if (!IPAddress.IsLoopback(address) && !IsLocalMachineAddress(address))
            {
                return false;
            }

            // A local socket peer alone is not proof of the local console: a page rebound to
            // 127.0.0.1 also connects from loopback. Require the request to be addressed to
            // this machine by name too.
            return IsExpectedLocalHost(context);
        }

        private static bool IsLocalMachineAddress(IPAddress address)
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    var localAddress = unicast.Address;
                    if (localAddress.IsIPv4MappedToIPv6)
                    {
                        localAddress = localAddress.MapToIPv4();
                    }

                    if (localAddress.AddressFamily == AddressFamily.InterNetwork &&
                        localAddress.Equals(address))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string ReadDeviceToken(HttpContext context)
        {
            var headerValue = context.Request.Headers[DeviceTrustService.HeaderName].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                return headerValue;
            }

            var queryValue = context.Request.Query["deviceToken"].ToString();
            if (!string.IsNullOrWhiteSpace(queryValue))
            {
                return queryValue;
            }

            return context.Request.Cookies[DeviceTrustService.CookieName];
        }

        private static string GetRemoteAddress(HttpContext context)
        {
            return context.Connection.RemoteIpAddress?.ToString() ?? "";
        }

        private static string NormalizeDeckMode(string mode)
        {
            return string.Equals(mode, "quota", StringComparison.OrdinalIgnoreCase)
                ? "quota"
                : "sideboard";
        }

        private static string BuildLocalDeckUrl(string mode)
        {
            var builder = new UriBuilder("http", "127.0.0.1", 5000, "index.html")
            {
                Query = $"mode={Uri.EscapeDataString(NormalizeDeckMode(mode))}&deck=1"
            };
            return builder.Uri.ToString();
        }

        private static string BuildAgyOAuthRedirectUri(HttpRequest request)
        {
            var port = request.Host.Port ?? 5000;
            return $"http://127.0.0.1:{port}";
        }

        private static bool IsFalseValue(string value)
        {
            return string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "no", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAgyOAuthCallbackRequest(HttpRequest request)
        {
            return !string.IsNullOrWhiteSpace(request.Query["state"].ToString()) &&
                (!string.IsNullOrWhiteSpace(request.Query["code"].ToString()) ||
                    !string.IsNullOrWhiteSpace(request.Query["error"].ToString()));
        }

        private static async Task WriteAgyOAuthCallbackAsync(HttpContext context)
        {
            var quotas = context.RequestServices.GetRequiredService<AiQuotaService>();
            var result = await quotas.CompleteAgyOAuthAsync(
                context.Request.Query["state"].ToString(),
                context.Request.Query["code"].ToString(),
                context.Request.Query["error"].ToString(),
                context.Request.Query["error_description"].ToString(),
                context.RequestAborted);
            context.Response.StatusCode = result.Success
                ? StatusCodes.Status200OK
                : StatusCodes.Status400BadRequest;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildAgyOAuthCallbackHtml(result));
        }

        private static string BuildAgyOAuthCallbackHtml(AgyQuotaService.AgyOAuthCallbackResult result)
        {
            var title = result.Success ? "AGY sign-in complete" : "AGY sign-in failed";
            var message = WebUtility.HtmlEncode(result.Message ?? title);
            var email = WebUtility.HtmlEncode(result.Email ?? string.Empty);
            var className = result.Success ? "ok" : "bad";
            var closeScript = result.Success
                ? "<script>setTimeout(function(){ window.close(); }, 1800);</script>"
                : string.Empty;

            return $@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
  <title>{WebUtility.HtmlEncode(title)}</title>
  <style>
    body {{
      margin: 0;
      min-height: 100vh;
      display: grid;
      place-items: center;
      font-family: Segoe UI, system-ui, sans-serif;
      color: #172033;
      background: #f5f7fb;
    }}
    main {{
      width: min(420px, calc(100vw - 32px));
      border: 1px solid #d9e1ee;
      border-radius: 10px;
      padding: 28px;
      background: #fff;
      box-shadow: 0 20px 60px rgba(32, 45, 70, 0.14);
    }}
    h1 {{
      margin: 0 0 10px;
      font-size: 22px;
      letter-spacing: 0;
    }}
    p {{
      margin: 8px 0 0;
      color: #526178;
      line-height: 1.45;
    }}
    .ok {{ color: #16834b; }}
    .bad {{ color: #b42318; }}
  </style>
</head>
<body>
  <main>
    <h1 class=""{className}"">{WebUtility.HtmlEncode(title)}</h1>
    <p>{message}</p>
    <p>{email}</p>
  </main>
  {closeScript}
</body>
</html>";
        }

    }

    public sealed class InputEvent
    {
        public string Type { get; set; }
        public string DeviceName { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public int Buttons { get; set; }
        public string Text { get; set; }
        public string Key { get; set; }
        public string Code { get; set; }
        public bool CtrlKey { get; set; }
        public bool AltKey { get; set; }
        public bool ShiftKey { get; set; }
        public bool MetaKey { get; set; }
    }

    public sealed class EnableDisplayRequest
    {
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;
        public int RefreshRate { get; set; } = 60;
    }

    public sealed class SetDisplayModeRequest
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int RefreshRate { get; set; } = 60;
    }

    public sealed class DeckLaunchRequest
    {
        public string Mode { get; set; } = "sideboard";
    }

    public sealed class WebRtcOfferRequest
    {
        public string Sdp { get; set; }
        public string DeviceName { get; set; }
        public int Fps { get; set; } = 45;
        public int Quality { get; set; } = 56;
        public int ReceiverMaxBitrateKbps { get; set; }
    }

    public sealed class AgyAccountRequest
    {
        public string AccountId { get; set; }
        public string Email { get; set; }
    }

    public sealed class CodexAccountRequest
    {
        public string AccountId { get; set; }
        public string Email { get; set; }
    }

    public sealed class PublicEndpointRequest
    {
        public string PublicUrl { get; set; }
    }
}
