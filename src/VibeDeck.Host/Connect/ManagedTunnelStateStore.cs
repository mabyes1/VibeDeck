using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VibeDeck.Host.Connect
{
    internal sealed class ManagedTunnelStateStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VibeDeck.ManagedTunnel.v1");
        private readonly object gate = new object();
        private readonly string statePath;
        private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public ManagedTunnelStateStore(string pathOverride = null)
        {
            statePath = string.IsNullOrWhiteSpace(pathOverride)
                ? Path.Combine(AppPaths.EnsureDirectory(AppPaths.ConnectDirectory), "managed-tunnel.json")
                : Path.GetFullPath(pathOverride);
        }

        public ManagedTunnelState LoadOrCreate(string installationId)
        {
            lock (gate)
            {
                var state = Load();
                if (state != null && string.Equals(state.InstallationId, installationId, StringComparison.Ordinal))
                {
                    return state;
                }

                state = new ManagedTunnelState
                {
                    Version = 1,
                    InstallationId = installationId,
                    ProvisioningSecret = CreateSecret(),
                    UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
                };
                Save(state);
                return state;
            }
        }

        public ManagedTunnelState SaveProvisioned(
            ManagedTunnelState state,
            string publicUrl,
            string tunnelId,
            string tunnelToken)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            lock (gate)
            {
                state.PublicUrl = publicUrl ?? string.Empty;
                state.TunnelId = tunnelId ?? string.Empty;
                state.TunnelToken = tunnelToken ?? string.Empty;
                state.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
                Save(state);
                return state;
            }
        }

        private ManagedTunnelState Load()
        {
            try
            {
                if (!File.Exists(statePath)) return null;
                var persisted = JsonSerializer.Deserialize<PersistedManagedTunnelState>(
                    File.ReadAllText(statePath, Encoding.UTF8),
                    jsonOptions);
                if (persisted == null || persisted.Version != 1 || string.IsNullOrWhiteSpace(persisted.InstallationId))
                {
                    return null;
                }

                return new ManagedTunnelState
                {
                    Version = persisted.Version,
                    InstallationId = persisted.InstallationId,
                    PublicUrl = persisted.PublicUrl ?? string.Empty,
                    TunnelId = persisted.TunnelId ?? string.Empty,
                    ProvisioningSecret = Unprotect(persisted.ProtectedProvisioningSecret),
                    TunnelToken = Unprotect(persisted.ProtectedTunnelToken),
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
                return null;
            }
        }

        private void Save(ManagedTunnelState state)
        {
            var directory = Path.GetDirectoryName(statePath);
            Directory.CreateDirectory(directory);
            var persisted = new PersistedManagedTunnelState
            {
                Version = 1,
                InstallationId = state.InstallationId,
                PublicUrl = state.PublicUrl ?? string.Empty,
                TunnelId = state.TunnelId ?? string.Empty,
                ProtectedProvisioningSecret = Protect(state.ProvisioningSecret),
                ProtectedTunnelToken = Protect(state.TunnelToken),
                UpdatedAt = state.UpdatedAt ?? DateTimeOffset.UtcNow.ToString("O")
            };
            var temporaryPath = statePath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(persisted, jsonOptions), new UTF8Encoding(false));
            try
            {
                if (File.Exists(statePath))
                {
                    File.Replace(temporaryPath, statePath, statePath + ".bak", true);
                }
                else
                {
                    File.Move(temporaryPath, statePath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private static string CreateSecret()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string Protect(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(value),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        private static string Unprotect(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(value),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }

        private sealed class PersistedManagedTunnelState
        {
            public int Version { get; set; }
            public string InstallationId { get; set; }
            public string PublicUrl { get; set; }
            public string TunnelId { get; set; }
            public string ProtectedProvisioningSecret { get; set; }
            public string ProtectedTunnelToken { get; set; }
            public string UpdatedAt { get; set; }
        }
    }

    internal sealed class ManagedTunnelState
    {
        public int Version { get; set; }
        public string InstallationId { get; set; }
        public string PublicUrl { get; set; }
        public string TunnelId { get; set; }
        public string ProvisioningSecret { get; set; }
        public string TunnelToken { get; set; }
        public string UpdatedAt { get; set; }
        public bool IsProvisioned =>
            !string.IsNullOrWhiteSpace(PublicUrl) &&
            !string.IsNullOrWhiteSpace(TunnelId) &&
            !string.IsNullOrWhiteSpace(TunnelToken);
    }
}
