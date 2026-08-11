using System;
using System.IO;
using System.Text.Json;
using VibeDeck.Host.Quotas;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class AiQuotaServiceTests
    {
        [Fact]
        public void ReadsCodexCreditBalanceFromLatestRateLimitEvent()
        {
            var fixturePath = Path.Combine(Path.GetTempPath(), $"vibedeck-codex-quota-{Guid.NewGuid():N}.jsonl");
            File.WriteAllLines(fixturePath, new[]
            {
                "{\"timestamp\":\"2026-07-17T06:40:00Z\",\"payload\":{\"rate_limits\":{\"credits\":{\"balance\":\"100.5\",\"unlimited\":false},\"primary\":{\"used_percent\":10,\"window_minutes\":300,\"resets_at\":1780000000}}}}",
                "{\"timestamp\":\"2026-07-17T06:48:41Z\",\"payload\":{\"rate_limits\":{\"credits\":{\"balance\":\"2250.7270450000\",\"unlimited\":false},\"primary\":{\"used_percent\":15,\"window_minutes\":300,\"resets_at\":1780000500}}}}"
            });

            try
            {
                var status = CodexQuotaReader.TryReadCodexQuotaFromFile(fixturePath);

                Assert.Equal(2250.727045d, status.CreditBalance.GetValueOrDefault(), 6);
                Assert.False(status.CreditUnlimited.GetValueOrDefault(true));
            }
            finally
            {
                File.Delete(fixturePath);
            }
        }

        [Fact]
        public void ParsesCodexAccountUsageWithoutSwitchingActiveProfile()
        {
            using var document = JsonDocument.Parse(
                "{\"plan_type\":\"plus\",\"rate_limit\":{" +
                "\"primary_window\":{\"used_percent\":25,\"limit_window_seconds\":18000,\"reset_after_seconds\":120}," +
                "\"secondary_window\":{\"used_percent\":40,\"limit_window_seconds\":604800,\"reset_after_seconds\":3600}}," +
                "\"credits\":{\"balance\":\"12.5\",\"unlimited\":false}}");
            var observedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
            var credentials = new CodexAccountStore.CodexCredentials
            {
                AccountId = "account-1",
                Email = "member@example.com",
                Tier = "plus",
                AccessToken = "not-serialized"
            };

            var status = CodexQuotaReader.BuildCodexQuotaFromUsage(
                document.RootElement,
                credentials,
                observedAt,
                "fixture");

            Assert.NotNull(status);
            Assert.Equal("account-1", status.AccountId);
            Assert.Equal("member@example.com", status.AccountEmail);
            Assert.Equal(25d, status.Primary.UsedPercent);
            Assert.Equal(300, status.Primary.WindowMinutes);
            Assert.Equal(observedAt.AddSeconds(120), status.Primary.ResetsAt);
            Assert.Equal(40d, status.Secondary.UsedPercent);
            Assert.Equal(10080, status.Secondary.WindowMinutes);
            Assert.Equal(12.5d, status.CreditBalance);
            Assert.False(status.CreditUnlimited.GetValueOrDefault(true));
        }

        [Fact]
        public void OlderCodexSessionSnapshotCannotOverwriteNewerAccountUsageCache()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), $"vibedeck-codex-cache-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);
            try
            {
                var newer = new AiQuotaStatus
                {
                    Id = "codex-account-1",
                    Label = "Codex",
                    Family = "codex",
                    AccountId = "account-1",
                    AccountEmail = "member@example.com",
                    State = "ok",
                    ObservedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero),
                    Primary = new QuotaWindow { UsedPercent = 20 }
                };
                var older = new AiQuotaStatus
                {
                    Id = "codex-account-1",
                    Label = "Codex",
                    Family = "codex",
                    AccountId = "account-1",
                    AccountEmail = "member@example.com",
                    State = "ok",
                    ObservedAt = newer.ObservedAt.Value.AddMinutes(-10),
                    Primary = new QuotaWindow { UsedPercent = 80 }
                };

                CodexQuotaReader.WriteCodexQuotaCache(tempDirectory, newer);
                CodexQuotaReader.WriteCodexQuotaCache(tempDirectory, older);

                var cacheFile = Assert.Single(Directory.GetFiles(tempDirectory, "*.json"));
                var cached = JsonSerializer.Deserialize<AiQuotaStatus>(File.ReadAllText(cacheFile));
                Assert.Equal(newer.ObservedAt, cached.ObservedAt);
                Assert.Equal(20d, cached.Primary.UsedPercent);
            }
            finally
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        [Fact]
        public void AtomicallyReplacesCodexAuthFileFromSavedProfile()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), $"vibedeck-codex-profile-{Guid.NewGuid():N}");
            var sourcePath = Path.Combine(tempDirectory, "saved-profile.json");
            var destinationPath = Path.Combine(tempDirectory, "auth.json");
            Directory.CreateDirectory(tempDirectory);
            File.WriteAllText(sourcePath, "saved account");
            File.WriteAllText(destinationPath, "old account");

            try
            {
                CodexAccountStore.CopyAtomic(sourcePath, destinationPath);

                Assert.Equal("saved account", File.ReadAllText(destinationPath));
                Assert.False(File.Exists(destinationPath + ".tmp"));
            }
            finally
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        [Fact]
        public void CodexProfileSerializationDoesNotExposeLocalFilePath()
        {
            var profile = new CodexAccountStore.CodexProfile
            {
                AccountId = "account-1",
                Email = "member@example.com",
                Tier = "plus",
                Path = @"C:\\private\\auth.json",
                IsActive = true
            };

            var json = JsonSerializer.Serialize(profile);

            Assert.DoesNotContain("Path", json, StringComparison.Ordinal);
            Assert.DoesNotContain("private", json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CodexQuotaReauthPrefersEmailOverSharedWorkspaceAccountId()
        {
            Assert.False(CodexAccountStore.ProfileMatches(
                "shared-workspace-id",
                "alice@example.com",
                "shared-workspace-id",
                "bob@example.com"));
        }

        [Fact]
        public void CodexQuotaReauthAcceptsTheSelectedUserIdentity()
        {
            Assert.True(CodexAccountStore.ProfileMatches(
                "shared-workspace-id",
                "alice@example.com",
                "shared-workspace-id",
                "alice@example.com"));
        }

        [Fact]
        public void CodexQuotaReauthPkceMatchesRfc7636Example()
        {
            const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
            Assert.Equal(
                "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
                CodexOAuthReauthService.BuildCodeChallenge(verifier));
        }

        [Fact]
        public void CodexQuotaReauthAuthorizationUrlUsesOfficialPkceContract()
        {
            var url = CodexOAuthReauthService.BuildAuthorizationUrl(
                "http://localhost:1455/auth/callback",
                "challenge-value",
                "state-value");

            Assert.StartsWith("https://auth.openai.com/oauth/authorize?", url, StringComparison.Ordinal);
            Assert.Contains("client_id=app_EMoamEEZ73f0CkXaXp7hrann", url, StringComparison.Ordinal);
            Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A1455%2Fauth%2Fcallback", url, StringComparison.Ordinal);
            Assert.Contains("code_challenge=challenge-value", url, StringComparison.Ordinal);
            Assert.Contains("code_challenge_method=S256", url, StringComparison.Ordinal);
            Assert.Contains("id_token_add_organizations=true", url, StringComparison.Ordinal);
            Assert.Contains("codex_cli_simplified_flow=true", url, StringComparison.Ordinal);
            Assert.Contains("originator=codex_cli_rs", url, StringComparison.Ordinal);
            Assert.Contains("state=state-value", url, StringComparison.Ordinal);
        }

        [Fact]
        public void CodexQuotaReauthCallbackQueryDecodesCodeAndState()
        {
            var query = CodexOAuthReauthService.ParseQuery(
                "/auth/callback?code=abc%2F123&state=state-value&scope=openid+email");

            Assert.Equal("abc/123", query["code"]);
            Assert.Equal("state-value", query["state"]);
            Assert.Equal("openid email", query["scope"]);
        }

        [Fact]
        public void CodexQuotaReauthProfileMergeUsesOfficialAuthShapeAndPreservesMetadata()
        {
            const string existing = "{\"custom\":\"keep\",\"tokens\":{\"access_token\":\"old\",\"refresh_token\":\"old-refresh\"}}";
            var refreshedAt = new DateTimeOffset(2026, 8, 9, 3, 0, 0, TimeSpan.Zero);
            var merged = CodexAccountStore.MergeOAuthProfileJson(
                existing,
                new CodexOAuthReauthService.OAuthTokens
                {
                    IdToken = "new-id",
                    AccessToken = "new-access",
                    RefreshToken = "new-refresh"
                },
                "account-1",
                refreshedAt);

            using var document = JsonDocument.Parse(merged);
            var root = document.RootElement;
            Assert.Equal("chatgpt", root.GetProperty("auth_mode").GetString());
            Assert.Equal("keep", root.GetProperty("custom").GetString());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("OPENAI_API_KEY").ValueKind);
            Assert.Equal(refreshedAt, root.GetProperty("last_refresh").GetDateTimeOffset());
            var tokens = root.GetProperty("tokens");
            Assert.Equal("new-id", tokens.GetProperty("id_token").GetString());
            Assert.Equal("new-access", tokens.GetProperty("access_token").GetString());
            Assert.Equal("new-refresh", tokens.GetProperty("refresh_token").GetString());
            Assert.Equal("account-1", tokens.GetProperty("account_id").GetString());
        }

        [Fact]
        public void CodexQuotaIdentityDoesNotMergeDifferentUsersInSameWorkspace()
        {
            var alice = new AiQuotaStatus
            {
                AccountId = "shared-workspace-id",
                AccountEmail = "alice@example.com"
            };
            var bob = new AiQuotaStatus
            {
                AccountId = "shared-workspace-id",
                AccountEmail = "bob@example.com"
            };

            Assert.False(CodexQuotaReader.SameCodexAccount(alice, bob));
            Assert.NotEqual(
                CodexQuotaReader.CodexIdentityKey(alice.AccountId, alice.AccountEmail),
                CodexQuotaReader.CodexIdentityKey(bob.AccountId, bob.AccountEmail));
        }

        [Fact]
        public void CodexProfilesStayInCurrentUsersLocalApplicationData()
        {
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppPaths.ProductName,
                "codex",
                "profiles");

            Assert.Equal(expected, QuotaPaths.CodexProfileDirectory());
        }

        [Fact]
        public void CodexAccountSwitchNeverTargetsChatGptDesktopProcess()
        {
            Assert.All(CliProcessManager.CodexProcessNames, processName =>
                Assert.Equal("codex", processName, ignoreCase: true));
        }
    }
}
