using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VibeDeck.Host.Security
{
    internal sealed class TrustedDeviceStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        // Deterministic serialization the store MAC is computed over. Depends only on
        // record property order and values, so save -> load -> re-serialize is stable.
        private static readonly JsonSerializerOptions CanonicalJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        private const string TokenHmacPrefix = "hmac1:";
        private const int StoreFormatVersion = 2;
        private static readonly byte[] StoreKeyEntropy = Encoding.UTF8.GetBytes("VibeDeck.Devices.StoreKey.v1");

        private readonly string storePath;
        private readonly byte[] macKey;
        private readonly bool macEnforcedAtStartup;

        public TrustedDeviceStore(string devicesDirectory)
        {
            var root = AppPaths.EnsureDirectory(devicesDirectory);
            storePath = Path.Combine(root, "trusted-devices.json");
            var macKeyPath = Path.Combine(root, "trusted-devices.key");

            // The presence of the DPAPI key file marks that the MAC upgrade already ran:
            // from then on a plain-array (legacy) store is rejected as a downgrade attack.
            macEnforcedAtStartup = File.Exists(macKeyPath);
            if (ProtectedSecretFile.TryRead(macKeyPath, StoreKeyEntropy, out var key))
            {
                macKey = key;
            }
            else if (macEnforcedAtStartup)
            {
                // Key exists but was protected by a different Windows account. Run with a
                // session-only key and never write: overwriting the store or the key here
                // would destroy the owning account's pairings.
                macKey = RandomNumberGenerator.GetBytes(32);
                PersistenceDisabled = true;
            }
            else
            {
                macKey = RandomNumberGenerator.GetBytes(32);
                if (!ProtectedSecretFile.TryWrite(macKeyPath, StoreKeyEntropy, macKey))
                {
                    // Broken ACLs: keep trust working in memory, but do not persist MACs
                    // that no future process could ever verify.
                    PersistenceDisabled = true;
                }
            }
        }

        public bool PersistenceDisabled { get; }

        public List<TrustedDeviceRecord> Load(out bool storeFormatOutdated)
        {
            storeFormatOutdated = false;
            var sawStoreFile = false;
            foreach (var candidate in new[] { storePath, storePath + ".bak" })
            {
                try
                {
                    if (!File.Exists(candidate))
                    {
                        continue;
                    }

                    sawStoreFile = true;
                    var text = File.ReadAllText(candidate);
                    if (text.TrimStart().StartsWith("[", StringComparison.Ordinal))
                    {
                        // Legacy unauthenticated array store. Accepted exactly once (no MAC
                        // key on disk yet) so existing pairings survive the upgrade; after
                        // that a plain array is treated as a hand-written downgrade and
                        // rejected.
                        if (macEnforcedAtStartup)
                        {
                            Trace.TraceWarning(
                                $"VibeDeck device store '{candidate}' is in the pre-upgrade format but a store key " +
                                "already exists; refusing it as a downgrade. Pairings from this file are ignored.");
                            continue;
                        }

                        var legacy = JsonSerializer.Deserialize<List<TrustedDeviceRecord>>(text, JsonOptions);
                        if (legacy != null)
                        {
                            storeFormatOutdated = true;
                            return legacy;
                        }

                        continue;
                    }

                    using var document = JsonDocument.Parse(text);
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object ||
                        !TryGetPropertyInsensitive(root, "Mac", out var macElement) ||
                        !TryGetPropertyInsensitive(root, "Devices", out var devicesElement) ||
                        devicesElement.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    var loaded = JsonSerializer.Deserialize<List<TrustedDeviceRecord>>(
                        devicesElement.GetRawText(), JsonOptions);
                    if (loaded == null)
                    {
                        continue;
                    }

                    if (!VerifyMac(loaded, macElement.GetString()))
                    {
                        // Tampered (or written under another account's key): a record a
                        // local attacker hand-wrote with a self-computed token hash must
                        // never become a trusted remote-input device.
                        Trace.TraceWarning(
                            $"VibeDeck device store '{candidate}' failed its integrity check and was refused. " +
                            "This means the file was edited outside VibeDeck, or it belongs to another Windows " +
                            "account. Every pairing in it is ignored; re-pair from the PC to recover.");
                        continue;
                    }

                    return loaded;
                }
                catch (Exception error)
                {
                    // Try the last known-good backup before treating this as a new install.
                    Trace.TraceWarning($"VibeDeck device store '{candidate}' could not be read: {error.Message}");
                }
            }

            if (sawStoreFile)
            {
                // Distinguishable from a genuine first run: a store existed but nothing in it
                // could be trusted, so the owner is about to see zero paired devices.
                Trace.TraceWarning(
                    "VibeDeck found a device store but could not accept any of it; starting with no paired devices.");
            }

            return new List<TrustedDeviceRecord>();
        }

        public bool TryMatchTokenHash(string storedHash, string token, out string strengthenedHash)
        {
            strengthenedHash = null;
            if (string.IsNullOrEmpty(storedHash))
            {
                return false;
            }

            var keyedHash = HashDeviceToken(token);
            if (storedHash.StartsWith(TokenHmacPrefix, StringComparison.Ordinal))
            {
                return FixedTimeEquals(storedHash, keyedHash);
            }

            if (!FixedTimeEquals(storedHash, HashLegacyToken(token)))
            {
                return false;
            }

            if (!PersistenceDisabled)
            {
                strengthenedHash = keyedHash;
            }
            return true;
        }

        public string HashDeviceToken(string token)
        {
            using var hmac = new HMACSHA256(macKey);
            return TokenHmacPrefix + Convert.ToBase64String(
                hmac.ComputeHash(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        }

        public void Save(List<TrustedDeviceRecord> devices)
        {
            if (PersistenceDisabled)
            {
                return;
            }

            var directory = Path.GetDirectoryName(storePath);
            Directory.CreateDirectory(directory);
            var payload = new TrustedDeviceStoreFile
            {
                Version = StoreFormatVersion,
                Mac = ComputeMac(devices),
                Devices = devices
            };
            var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(storePath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(payload, JsonOptions));
                if (File.Exists(storePath))
                {
                    File.Replace(temporaryPath, storePath, storePath + ".bak", true);
                }
                else
                {
                    File.Move(temporaryPath, storePath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static bool TryGetPropertyInsensitive(JsonElement element, string name, out JsonElement value)
        {
            if (element.TryGetProperty(name, out value))
            {
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private string ComputeMac(List<TrustedDeviceRecord> records)
        {
            var canonical = JsonSerializer.Serialize(records, CanonicalJsonOptions);
            using var hmac = new HMACSHA256(macKey);
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
        }

        private bool VerifyMac(List<TrustedDeviceRecord> records, string mac)
        {
            if (string.IsNullOrWhiteSpace(mac))
            {
                return false;
            }

            return FixedTimeEquals(ComputeMac(records), mac);
        }

        private static string HashLegacyToken(string token)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(left ?? string.Empty),
                Encoding.UTF8.GetBytes(right ?? string.Empty));
        }

        private sealed class TrustedDeviceStoreFile
        {
            public int Version { get; set; }
            public string Mac { get; set; }
            public List<TrustedDeviceRecord> Devices { get; set; }
        }
    }
}
