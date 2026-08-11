using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VibeDeck.Host.Security;
using Xunit;

namespace VibeDeck.Host.Tests
{
    // Integrity guarantees of the trusted-device store: a MAC'd store round-trips,
    // a hand-edited record is rejected, a legacy (pre-MAC) file is accepted exactly
    // once and upgraded, and a downgrade back to the legacy format is rejected.
    public sealed class DeviceTrustStoreIntegrityTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "VibeDeck-tests", Guid.NewGuid().ToString("N"));

        private const string UserAgent = "Mozilla/5.0 (Linux; Android 14; K) Chrome/150 Mobile";
        private const string Address = "192.168.0.42";

        private string StorePath => Path.Combine(root, "trusted-devices.json");
        private string BackupPath => StorePath + ".bak";
        private string KeyPath => Path.Combine(root, "trusted-devices.key");

        private static string Sha256Base64(string token)
        {
            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }

        private string PairDevice(DeviceTrustService service, string clientId = "client-instance-integrity01")
        {
            var request = service.RequestApproval("Android 裝置", "Android", "SM-S9110", clientId, UserAgent, Address);
            Assert.True(service.ApproveRequest(request.RequestId).Success);
            var poll = service.PollApproval(request.RequestId, request.RequestSecret);
            Assert.Equal("approved", poll.Status);
            Assert.False(string.IsNullOrEmpty(poll.DeviceToken));
            return poll.DeviceToken;
        }

        private void DeleteBackup()
        {
            if (File.Exists(BackupPath))
            {
                File.Delete(BackupPath);
            }
        }

        [Fact]
        public void Valid_store_with_mac_survives_restart()
        {
            var token = PairDevice(new DeviceTrustService(root));

            using (var document = JsonDocument.Parse(File.ReadAllText(StorePath)))
            {
                Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
                Assert.True(document.RootElement.TryGetProperty("Mac", out var mac));
                Assert.False(string.IsNullOrWhiteSpace(mac.GetString()));
                Assert.Equal(2, document.RootElement.GetProperty("Version").GetInt32());
            }

            Assert.True(File.Exists(KeyPath));

            var restarted = new DeviceTrustService(root);
            Assert.True(restarted.IsTrusted(token, Address, UserAgent));
            Assert.Equal(1, restarted.GetStatus(null, "127.0.0.1", "test", true, false).PairedDeviceCount);
        }

        [Fact]
        public void Hand_written_record_with_self_computed_hash_is_rejected()
        {
            PairDevice(new DeviceTrustService(root));

            // A local attacker who can edit the JSON but cannot read the DPAPI MAC key
            // appends a record whose TokenHash they computed themselves (the pre-fix
            // attack: base64(SHA256(token)) is attacker-computable).
            const string forgedToken = "attacker-forged-token-000000000000000000";
            List<TrustedDeviceRecord> records;
            string mac;
            using (var document = JsonDocument.Parse(File.ReadAllText(StorePath)))
            {
                mac = document.RootElement.GetProperty("Mac").GetString();
                records = JsonSerializer.Deserialize<List<TrustedDeviceRecord>>(
                    document.RootElement.GetProperty("Devices").GetRawText());
            }

            records.Add(new TrustedDeviceRecord
            {
                DeviceId = "forged-device",
                Name = "Forged",
                TokenHash = Sha256Base64(forgedToken),
                CreatedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow
            });
            File.WriteAllText(StorePath, JsonSerializer.Serialize(new
            {
                Version = 2,
                Mac = mac,
                Devices = records
            }));
            DeleteBackup();

            var service = new DeviceTrustService(root);
            Assert.False(service.IsTrusted(forgedToken, Address, UserAgent));
            // The whole tampered store is rejected, not just the forged record.
            Assert.Equal(0, service.GetStatus(null, "127.0.0.1", "test", true, false).PairedDeviceCount);
        }

        [Fact]
        public void Legacy_unauthenticated_store_is_accepted_once_and_upgraded()
        {
            Directory.CreateDirectory(root);
            const string legacyToken = "legacy-device-token-123456789012345678901234";
            File.WriteAllText(StorePath, JsonSerializer.Serialize(new[]
            {
                new TrustedDeviceRecord
                {
                    DeviceId = "legacy-device",
                    Name = "Legacy Phone",
                    TokenHash = Sha256Base64(legacyToken),
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
                    LastSeenAt = DateTimeOffset.UtcNow.AddDays(-1),
                    LastRemoteAddress = Address,
                    LastUserAgent = UserAgent
                }
            }));

            var service = new DeviceTrustService(root);
            Assert.True(service.IsTrusted(legacyToken, Address, UserAgent));

            // Store is now MAC'd (object format) and the enforcement key exists.
            using (var document = JsonDocument.Parse(File.ReadAllText(StorePath)))
            {
                Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
                Assert.True(document.RootElement.TryGetProperty("Mac", out _));
            }

            Assert.True(File.Exists(KeyPath));

            var restarted = new DeviceTrustService(root);
            Assert.True(restarted.IsTrusted(legacyToken, Address, UserAgent));
        }

        [Fact]
        public void Legacy_format_downgrade_is_rejected_once_mac_is_enforced()
        {
            PairDevice(new DeviceTrustService(root));

            // After the upgrade the MAC key exists; an attacker rewriting the file back
            // to the pre-MAC array format must not re-open the laundering hole.
            const string forgedToken = "downgrade-forged-token-00000000000000000";
            File.WriteAllText(StorePath, JsonSerializer.Serialize(new[]
            {
                new TrustedDeviceRecord
                {
                    DeviceId = "forged-device",
                    Name = "Forged",
                    TokenHash = Sha256Base64(forgedToken),
                    CreatedAt = DateTimeOffset.UtcNow,
                    LastSeenAt = DateTimeOffset.UtcNow
                }
            }));
            DeleteBackup();

            var service = new DeviceTrustService(root);
            Assert.False(service.IsTrusted(forgedToken, Address, UserAgent));
            Assert.Equal(0, service.GetStatus(null, "127.0.0.1", "test", true, false).PairedDeviceCount);
        }

        [Fact]
        public void Tampering_with_an_existing_record_invalidates_the_store()
        {
            var token = PairDevice(new DeviceTrustService(root));

            var text = File.ReadAllText(StorePath);
            var tampered = text.Replace("\"Name\": \"Samsung SM-S9110\"", "\"Name\": \"Attacker Renamed\"");
            Assert.NotEqual(text, tampered);
            File.WriteAllText(StorePath, tampered);
            DeleteBackup();

            var service = new DeviceTrustService(root);
            Assert.False(service.IsTrusted(token, Address, UserAgent));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch
            {
            }
        }
    }
}
