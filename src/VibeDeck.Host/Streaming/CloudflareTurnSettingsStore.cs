using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VibeDeck.Host.Streaming
{
    /// <summary>
    /// Stores the long-lived Cloudflare TURN API token locally. The token is
    /// protected with the Windows account that runs the interactive Host and is
    /// never returned from an HTTP endpoint.
    /// </summary>
    public sealed class CloudflareTurnSettingsStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VibeDeck.CloudflareTurn.v1");
        private static readonly Regex KeyIdPattern = new Regex("^[A-Za-z0-9_-]{6,160}$", RegexOptions.Compiled);
        private readonly object gate = new object();
        private readonly string path;
        private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public CloudflareTurnSettingsStore(string pathOverride = null)
        {
            path = string.IsNullOrWhiteSpace(pathOverride)
                ? Path.Combine(AppPaths.EnsureDirectory(AppPaths.SecretsDirectory), "cloudflare-turn.json")
                : Path.GetFullPath(pathOverride);
        }

        public CloudflareTurnSettings Get()
        {
            lock (gate)
            {
                return Load();
            }
        }

        public CloudflareTurnSettings Configure(string keyId, string apiToken)
        {
            var normalizedKeyId = (keyId ?? string.Empty).Trim();
            var normalizedToken = (apiToken ?? string.Empty).Trim();
            if (!KeyIdPattern.IsMatch(normalizedKeyId))
            {
                throw new TurnSettingsException("turn.invalid_key_id", "Cloudflare TURN Key ID is invalid.");
            }
            if (normalizedToken.Length < 16 || normalizedToken.Length > 2048 || normalizedToken.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            {
                throw new TurnSettingsException("turn.invalid_api_token", "Cloudflare TURN API token is invalid.");
            }

            lock (gate)
            {
                var settings = new CloudflareTurnSettings
                {
                    KeyId = normalizedKeyId,
                    ApiToken = normalizedToken,
                    UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
                };
                Save(settings);
                return settings;
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    throw new TurnSettingsException("turn.clear_failed", "Could not remove the local TURN settings.", error);
                }
            }
        }

        private CloudflareTurnSettings Load()
        {
            try
            {
                if (!File.Exists(path)) return CloudflareTurnSettings.Empty;
                var persisted = JsonSerializer.Deserialize<PersistedSettings>(File.ReadAllText(path, Encoding.UTF8), jsonOptions);
                if (persisted == null || persisted.Version != 1 || !KeyIdPattern.IsMatch(persisted.KeyId ?? string.Empty))
                {
                    return CloudflareTurnSettings.Empty;
                }

                var token = Unprotect(persisted.ProtectedApiToken);
                return string.IsNullOrWhiteSpace(token)
                    ? CloudflareTurnSettings.Empty
                    : new CloudflareTurnSettings
                    {
                        KeyId = persisted.KeyId,
                        ApiToken = token,
                        UpdatedAt = persisted.UpdatedAt ?? string.Empty
                    };
            }
            catch (Exception error) when (
                error is IOException ||
                error is UnauthorizedAccessException ||
                error is JsonException ||
                error is CryptographicException ||
                error is FormatException)
            {
                return CloudflareTurnSettings.Empty;
            }
        }

        private void Save(CloudflareTurnSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var persisted = new PersistedSettings
                {
                    Version = 1,
                    KeyId = settings.KeyId,
                    ProtectedApiToken = Protect(settings.ApiToken),
                    UpdatedAt = settings.UpdatedAt
                };
                var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(persisted, jsonOptions), new UTF8Encoding(false));
                try
                {
                    if (File.Exists(path))
                    {
                        File.Replace(temporaryPath, path, path + ".bak", true);
                    }
                    else
                    {
                        File.Move(temporaryPath, path);
                    }
                }
                finally
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is CryptographicException)
            {
                throw new TurnSettingsException("turn.save_failed", "Could not save the local TURN settings.", error);
            }
        }

        private static string Protect(string value)
        {
            var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        private static string Unprotect(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }

        private sealed class PersistedSettings
        {
            public int Version { get; set; }
            public string KeyId { get; set; }
            public string ProtectedApiToken { get; set; }
            public string UpdatedAt { get; set; }
        }
    }

    public sealed class CloudflareTurnSettings
    {
        public static readonly CloudflareTurnSettings Empty = new CloudflareTurnSettings();

        public string KeyId { get; set; } = string.Empty;
        public string ApiToken { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
        public bool IsConfigured => !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(ApiToken);

        public string MaskedKeyId => string.IsNullOrWhiteSpace(KeyId)
            ? string.Empty
            : KeyId.Length <= 8 ? "••••" : KeyId.Substring(0, 4) + "…" + KeyId.Substring(KeyId.Length - 4);
    }

    public sealed class TurnSettingsException : Exception
    {
        public TurnSettingsException(string code, string message, Exception innerException = null)
            : base(message, innerException)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
