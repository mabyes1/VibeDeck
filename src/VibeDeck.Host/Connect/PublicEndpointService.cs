using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using VibeDeck.Host.Diagnostics;

namespace VibeDeck.Host.Connect
{
    /// <summary>
    /// Stores the one browser-trusted public hostname assigned to this Host.
    /// Cloudflare credentials never enter this process; a local installer or
    /// control plane only writes the resulting HTTPS URL after provisioning it.
    /// </summary>
    public sealed class PublicEndpointService
    {
        public const string OriginalRemoteAddressItemKey = "VibeDeck.OriginalRemoteAddress";

        private const string DefaultBaseDomain = "vibedeck.pp.ua";
        private readonly object gate = new object();
        private readonly string storePath;
        private readonly string backupPath;
        private readonly string baseDomain;
        private readonly AuditTrailService audit;
        private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        private PublicEndpointStore store;

        public PublicEndpointService(IConfiguration configuration, AuditTrailService audit)
            : this(
                null,
                configuration?["PublicEndpoint:BaseDomain"]
                    ?? Environment.GetEnvironmentVariable("VIBEDECK_PUBLIC_BASE_DOMAIN"),
                audit)
        {
        }

        public PublicEndpointService()
            : this(null, null, null)
        {
        }

        public PublicEndpointService(string storePathOverride, string baseDomainOverride)
            : this(storePathOverride, baseDomainOverride, null)
        {
        }

        private PublicEndpointService(string storePathOverride, string baseDomainOverride, AuditTrailService audit)
        {
            var directory = AppPaths.EnsureDirectory(AppPaths.ConnectDirectory);
            storePath = string.IsNullOrWhiteSpace(storePathOverride)
                ? Path.Combine(directory, "public-endpoint.json")
                : storePathOverride;
            backupPath = storePath + ".bak";
            baseDomain = NormalizeBaseDomain(baseDomainOverride);
            this.audit = audit;
            store = Load();

            var previousId = store.InstallationId;
            var stateChanged = false;
            if (!IsValidInstallationId(store.InstallationId))
            {
                store.InstallationId = CreateInstallationId();
                store.PublicUrl = null;
                store.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
                stateChanged = true;
            }
            else if (!string.IsNullOrWhiteSpace(store.PublicUrl))
            {
                try
                {
                    var normalized = NormalizePublicUrl(store.PublicUrl);
                    if (!string.Equals(store.PublicUrl, normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        store.PublicUrl = normalized;
                        store.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
                        stateChanged = true;
                    }
                }
                catch (PublicEndpointException error)
                {
                    store.PublicUrl = null;
                    store.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
                    stateChanged = true;
                    audit?.Record(
                        "warning",
                        "public-endpoint",
                        "load",
                        "cleared-invalid-url",
                        subject: store.InstallationId,
                        details: new Dictionary<string, string>
                        {
                            ["code"] = error.Code
                        });
                }
            }

            if (stateChanged)
            {
                TryPersistInitialState(previousId);
            }
        }

        public PublicEndpointConfiguration GetConfiguration()
        {
            lock (gate)
            {
                return Snapshot();
            }
        }

        public PublicEndpointConfiguration Configure(string publicUrl)
        {
            lock (gate)
            {
                var normalized = NormalizePublicUrl(publicUrl);
                if (string.Equals(store.PublicUrl, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return Snapshot();
                }

                var previousUrl = store.PublicUrl;
                var previousUpdatedAt = store.UpdatedAt;
                store.PublicUrl = normalized;
                store.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
                try
                {
                    Persist();
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    store.PublicUrl = previousUrl;
                    store.UpdatedAt = previousUpdatedAt;
                    audit?.RecordException("public-endpoint", "configure", error, subject: ExpectedHostName);
                    throw new PublicEndpointException(
                        "安全網址無法寫入磁碟，請確認 VibeDeck 資料夾權限後再試。",
                        "public_endpoint.persistence_failed",
                        error);
                }

                return Snapshot();
            }
        }

        public PublicEndpointConfiguration Clear()
        {
            lock (gate)
            {
                if (string.IsNullOrWhiteSpace(store.PublicUrl))
                {
                    return Snapshot();
                }

                var previousUrl = store.PublicUrl;
                var previousUpdatedAt = store.UpdatedAt;
                store.PublicUrl = null;
                store.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
                try
                {
                    Persist();
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    store.PublicUrl = previousUrl;
                    store.UpdatedAt = previousUpdatedAt;
                    audit?.RecordException("public-endpoint", "clear", error, subject: ExpectedHostName);
                    throw new PublicEndpointException(
                        "安全網址設定無法清除，請確認 VibeDeck 資料夾權限後再試。",
                        "public_endpoint.persistence_failed",
                        error);
                }

                return Snapshot();
            }
        }

        /// <summary>
        /// A public pairing request is accepted only when it was forwarded by a
        /// loopback connector and retains the exact configured HTTPS hostname.
        /// This prevents a LAN client from spoofing X-Forwarded-* headers.
        /// </summary>
        public bool IsTrustedPublicRequest(HttpContext context)
        {
            if (context == null || !context.Request.IsHttps)
            {
                return false;
            }

            if (!(context.Items[OriginalRemoteAddressItemKey] is IPAddress sourceAddress))
            {
                return false;
            }

            if (sourceAddress.IsIPv4MappedToIPv6)
            {
                sourceAddress = sourceAddress.MapToIPv4();
            }

            if (!IPAddress.IsLoopback(sourceAddress))
            {
                return false;
            }

            PublicEndpointConfiguration configuration;
            lock (gate)
            {
                configuration = Snapshot();
            }

            if (!configuration.IsConfigured ||
                !Uri.TryCreate(configuration.PublicUrl, UriKind.Absolute, out var endpoint))
            {
                return false;
            }

            if (!string.Equals(context.Request.Host.Host, endpoint.IdnHost, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var requestPort = context.Request.Host.Port ?? 443;
            return requestPort == endpoint.Port;
        }

        private string ExpectedHostName => $"{store.InstallationId}.{baseDomain}";

        private PublicEndpointConfiguration Snapshot()
        {
            return new PublicEndpointConfiguration
            {
                InstallationId = store.InstallationId,
                BaseDomain = baseDomain,
                PublicUrl = store.PublicUrl ?? string.Empty,
                UpdatedAt = store.UpdatedAt ?? string.Empty
            };
        }

        private string NormalizePublicUrl(string value)
        {
            var candidate = (value ?? string.Empty).Trim();
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var endpoint))
            {
                throw new PublicEndpointException("請輸入完整的 HTTPS 安全網址。", "public_endpoint.invalid_url");
            }

            if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new PublicEndpointException("安全網址必須使用 HTTPS。", "public_endpoint.https_required");
            }

            if (!string.IsNullOrWhiteSpace(endpoint.UserInfo) ||
                endpoint.Port != 443 ||
                !string.IsNullOrEmpty(endpoint.Query) ||
                !string.IsNullOrEmpty(endpoint.Fragment) ||
                !string.Equals(endpoint.AbsolutePath, "/", StringComparison.Ordinal))
            {
                throw new PublicEndpointException(
                    "安全網址不可包含帳號、連接埠、路徑、參數或片段。",
                    "public_endpoint.invalid_shape");
            }

            if (!string.Equals(endpoint.IdnHost, ExpectedHostName, StringComparison.OrdinalIgnoreCase))
            {
                throw new PublicEndpointException(
                    $"此電腦只接受 https://{ExpectedHostName}/。",
                    "public_endpoint.unexpected_host");
            }

            return $"https://{ExpectedHostName}/";
        }

        private PublicEndpointStore Load()
        {
            var primary = ReadStore(storePath, out var primaryError);
            if (primary != null)
            {
                return primary;
            }

            var backup = ReadStore(backupPath, out var backupError);
            if (backup != null)
            {
                TryRestoreBackup();
                audit?.Record(
                    "warning",
                    "public-endpoint",
                    "load",
                    "recovered-from-backup",
                    details: new Dictionary<string, string>
                    {
                        ["primaryError"] = primaryError ?? "primary-missing"
                    });
                return backup;
            }

            if (!string.IsNullOrWhiteSpace(primaryError))
            {
                audit?.Record(
                    "error",
                    "public-endpoint",
                    "load",
                    "unreadable",
                    details: new Dictionary<string, string>
                    {
                        ["primaryError"] = primaryError,
                        ["backupError"] = backupError ?? "backup-missing"
                    });
            }

            return new PublicEndpointStore();
        }

        private PublicEndpointStore ReadStore(string path, out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text))
                {
                    error = "public endpoint file is empty";
                    return null;
                }

                var loaded = JsonSerializer.Deserialize<PublicEndpointStore>(text, jsonOptions);
                if (loaded == null)
                {
                    error = "public endpoint file contains no data";
                }

                return loaded;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                error = exception.Message;
                return null;
            }
        }

        private void Persist()
        {
            var directory = Path.GetDirectoryName(storePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(store, jsonOptions));
                if (File.Exists(storePath))
                {
                    File.Copy(storePath, backupPath, true);
                    File.Move(temporaryPath, storePath, true);
                }
                else
                {
                    File.Move(temporaryPath, storePath);
                    File.Copy(storePath, backupPath, true);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private void TryPersistInitialState(string previousId)
        {
            try
            {
                Persist();
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                audit?.RecordException(
                    "public-endpoint",
                    "create-installation-id",
                    error,
                    subject: store.InstallationId,
                    details: new Dictionary<string, string>
                    {
                        ["previousId"] = previousId ?? "missing"
                    });
            }
        }

        private void TryRestoreBackup()
        {
            try
            {
                if (File.Exists(storePath))
                {
                    var corruptPath = storePath + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json";
                    File.Copy(storePath, corruptPath, false);
                }

                File.Copy(backupPath, storePath, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string NormalizeBaseDomain(string value)
        {
            var candidate = (value ?? DefaultBaseDomain).Trim().Trim('.').ToLowerInvariant();
            return Uri.CheckHostName(candidate) == UriHostNameType.Dns && candidate.Contains('.')
                ? candidate
                : DefaultBaseDomain;
        }

        private static bool IsValidInstallationId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 19 || !value.StartsWith("vd-", StringComparison.Ordinal))
            {
                return false;
            }

            for (var index = 3; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }

        private static string CreateInstallationId()
        {
            var bytes = new byte[8];
            RandomNumberGenerator.Fill(bytes);
            return "vd-" + Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private sealed class PublicEndpointStore
        {
            public string InstallationId { get; set; }
            public string PublicUrl { get; set; }
            public string UpdatedAt { get; set; }
        }
    }

    public sealed class PublicEndpointConfiguration
    {
        public string InstallationId { get; set; }
        public string BaseDomain { get; set; }
        public string PublicUrl { get; set; }
        public string UpdatedAt { get; set; }
        public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicUrl);
    }

    public sealed class PublicEndpointException : Exception
    {
        public PublicEndpointException(string message, string code = "")
            : base(message)
        {
            Code = code ?? string.Empty;
        }

        public PublicEndpointException(string message, Exception innerException)
            : base(message, innerException)
        {
            Code = string.Empty;
        }

        public PublicEndpointException(string message, string code, Exception innerException)
            : base(message, innerException)
        {
            Code = code ?? string.Empty;
        }

        public string Code { get; }
    }
}
