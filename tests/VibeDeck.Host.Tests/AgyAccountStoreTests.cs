using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using VibeDeck.Host.Quotas;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class AgyAccountStoreTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "VibeDeck-tests", Guid.NewGuid().ToString("N"));

        [Fact]
        public void Write_round_trips_protected_refresh_token_and_reuses_existing_account_file()
        {
            var store = Path.Combine(root, "store");
            AgyAccountStore.Write(store, new AgyAccountToken
            {
                AccountId = "account-1",
                Email = "first@example.com",
                Tier = "pro",
                RefreshToken = "refresh-token-1"
            });

            AgyAccountStore.Write(store, new AgyAccountToken
            {
                AccountId = "account-1",
                Email = "updated@example.com",
                Tier = "pro",
                RefreshToken = "refresh-token-2"
            });

            var files = Directory.GetFiles(store, "*.json");
            Assert.Single(files);
            var account = Assert.Single(AgyAccountStore.Read(store));
            Assert.Equal("account-1", account.AccountId);
            Assert.Equal("updated@example.com", account.Email);
            Assert.Equal("refresh-token-2", account.RefreshToken);

            using var document = JsonDocument.Parse(File.ReadAllText(files[0]));
            Assert.False(document.RootElement.TryGetProperty("refresh_token", out _));
            Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("refresh_token_protected").GetString()));
        }

        [Fact]
        public void Import_from_antigravity_preserves_identity_tier_and_refresh_token()
        {
            var cockpit = Path.Combine(root, "cockpit");
            var store = Path.Combine(root, "store");
            Directory.CreateDirectory(cockpit);
            File.WriteAllText(Path.Combine(cockpit, "source.json"), JsonSerializer.Serialize(new
            {
                id = "agy-account-42",
                email = "agy@example.com",
                token = new { refresh_token = "agy-refresh-token" },
                quota = new { subscription_tier = "premium" }
            }));

            var imported = AgyAccountStore.ImportFromAntigravity(store, cockpit);

            Assert.Equal(1, imported);
            var account = Assert.Single(AgyAccountStore.Read(store));
            Assert.Equal("agy-account-42", account.AccountId);
            Assert.Equal("agy@example.com", account.Email);
            Assert.Equal("premium", account.Tier);
            Assert.Equal("agy-refresh-token", account.RefreshToken);
        }

        [Fact]
        public void Find_matching_files_accepts_either_account_id_or_email()
        {
            var store = Path.Combine(root, "store");
            AgyAccountStore.Write(store, new AgyAccountToken
            {
                AccountId = "account-1",
                Email = "agy@example.com",
                RefreshToken = "refresh-token"
            });

            Assert.Single(AgyAccountStore.FindMatchingFiles(store, "account-1", null));
            Assert.Single(AgyAccountStore.FindMatchingFiles(store, null, "AGY@example.com"));
            Assert.Empty(AgyAccountStore.FindMatchingFiles(store, "missing", "missing@example.com"));
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
