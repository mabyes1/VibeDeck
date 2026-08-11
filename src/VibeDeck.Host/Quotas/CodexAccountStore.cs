using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using static VibeDeck.Host.Quotas.QuotaJsonHelpers;
using static VibeDeck.Host.Quotas.QuotaPaths;

namespace VibeDeck.Host.Quotas
{
    /// <summary>
    /// Multi-account support for the Codex CLI, which only ever reads a single
    /// ~/.codex/auth.json. This store "captures" the currently logged-in auth.json
    /// into a named profile library so accounts are not lost when you log into
    /// another, and swaps a chosen profile back into auth.json to switch.
    ///
    /// Switch sequence (order matters): stop the running Codex CLI FIRST (a live
    /// session's token refresh would clobber the swapped file), capture current,
    /// swap, then relaunch.
    /// </summary>
    internal static class CodexAccountStore
    {
        private static readonly object ProfileFileLock = new object();
        private static readonly object QuotaReauthLock = new object();
        private static readonly Dictionary<string, CodexQuotaReauthSession> QuotaReauthSessions =
            new Dictionary<string, CodexQuotaReauthSession>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan QuotaReauthLifetime = TimeSpan.FromMinutes(12);

        internal sealed class CodexProfile
        {
            public string AccountId { get; set; }
            public string Email { get; set; }
            public string Tier { get; set; }
            public DateTimeOffset? LastUsedAt { get; set; }
            internal string Path { get; set; }
            public bool IsActive { get; set; }
        }

        internal sealed class CodexCredentials
        {
            public string AccountId { get; set; }
            public string Email { get; set; }
            public string Tier { get; set; }
            public string AccessToken { get; set; }
            internal string Path { get; set; }
        }

        internal sealed class CodexActionResult
        {
            public bool Success { get; set; }
            public string AccountId { get; set; }
            public string Email { get; set; }
            public string Message { get; set; }
            public int Affected { get; set; }
            public string Code { get; set; }
            public string SessionId { get; set; }
            public string State { get; set; }
            public DateTimeOffset? ExpiresAt { get; set; }

            public static CodexActionResult Fail(string code, string message) =>
                new CodexActionResult { Success = false, Code = code, Message = message };
        }

        private sealed class CodexQuotaReauthSession
        {
            public string SessionId { get; set; }
            public string AccountId { get; set; }
            public string Email { get; set; }
            public string ProfilePath { get; set; }
            public DateTimeOffset ExpiresAt { get; set; }
        }

        /// <summary>
        /// Snapshots the current ~/.codex/auth.json into the profile library so the
        /// active account survives a future switch. No-op if not logged in.
        /// Returns the captured account id, or null.
        /// </summary>
        internal static string CaptureCurrent()
        {
            lock (ProfileFileLock)
            {
                return CaptureCurrentCore();
            }
        }

        private static string CaptureCurrentCore()
        {
            var authFile = CodexAuthFile();
            if (!File.Exists(authFile))
            {
                return null;
            }

            var (accountId, email, _) = CodexQuotaReader.ReadAuthFileIdentity(authFile);
            var key = CodexQuotaReader.CodexIdentityKey(accountId, email);
            if (string.IsNullOrWhiteSpace(key))
            {
                return null; // not a logged-in account, nothing worth capturing
            }

            var profileDir = CodexProfileDirectory();
            Directory.CreateDirectory(profileDir);
            var target = System.IO.Path.Combine(profileDir, $"{SafeFileName(key)}.json");
            try
            {
                CopyAtomic(authFile, target);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }

            return key;
        }

        internal static IReadOnlyList<CodexProfile> ListProfiles()
        {
            CaptureCurrent();

            return ReadProfiles();
        }

        private static IReadOnlyList<CodexProfile> ReadProfiles()
        {
            var authFile = CodexAuthFile();
            var (activeId, activeEmail, _) = File.Exists(authFile)
                ? CodexQuotaReader.ReadAuthFileIdentity(authFile)
                : (null, null, null);
            var activeKey = CodexQuotaReader.CodexIdentityKey(activeId, activeEmail);

            var profiles = new List<CodexProfile>();
            var dir = CodexProfileDirectory();
            foreach (var file in FindJsonFiles(dir))
            {
                var (accountId, email, tier) = CodexQuotaReader.ReadAuthFileIdentity(file);
                var key = CodexQuotaReader.CodexIdentityKey(accountId, email, System.IO.Path.GetFileNameWithoutExtension(file));
                var cached = CodexQuotaReader.ReadCachedQuotaForAccount(accountId, email);
                profiles.Add(new CodexProfile
                {
                    AccountId = accountId,
                    Email = email,
                    Tier = tier,
                    LastUsedAt = cached?.ObservedAt,
                    Path = file,
                    IsActive = !string.IsNullOrWhiteSpace(activeKey) &&
                        string.Equals(key, activeKey, StringComparison.OrdinalIgnoreCase)
                });
            }

            return profiles
                .GroupBy(p => CodexQuotaReader.CodexIdentityKey(p.AccountId, p.Email, p.Path), StringComparer.OrdinalIgnoreCase)
                .Select(g => g
                    .OrderByDescending(p => p.LastUsedAt ?? DateTimeOffset.MinValue)
                    .First())
                .OrderByDescending(p => p.LastUsedAt ?? DateTimeOffset.MinValue)
                .ThenBy(p => p.Email ?? p.AccountId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static CodexCredentials ReadCredentials(string accountId, string email)
        {
            CaptureCurrent();
            var profile = FindProfile(accountId, email);
            if (profile == null || string.IsNullOrWhiteSpace(profile.Path) || !File.Exists(profile.Path))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(profile.Path));
                var root = document.RootElement;
                if (!TryGetProperty(root, "tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var accessToken = TryGetString(tokens, "access_token");
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    return null;
                }

                return new CodexCredentials
                {
                    AccountId = profile.AccountId,
                    Email = profile.Email,
                    Tier = profile.Tier,
                    AccessToken = accessToken,
                    Path = profile.Path
                };
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Starts an in-process Codex OAuth + PKCE flow used only to refresh the
        /// stored VibeDeck profile for one quota account. The user's active
        /// ~/.codex/auth.json and running Codex session are never switched.
        /// </summary>
        internal static CodexActionResult StartQuotaReAuth(string accountId, string email)
        {
            CaptureCurrent();
            var profile = FindProfile(accountId, email);
            var targetAccountId = FirstNonEmpty(profile?.AccountId, accountId);
            var targetEmail = FirstNonEmpty(profile?.Email, email);
            var targetKey = CodexQuotaReader.CodexIdentityKey(targetAccountId, targetEmail);
            if (string.IsNullOrWhiteSpace(targetKey))
            {
                return CodexActionResult.Fail("quota.codex_profile_required", "Select a Codex account first.");
            }

            var targetPath = profile?.Path;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                try
                {
                    var profileDir = CodexProfileDirectory();
                    Directory.CreateDirectory(profileDir);
                    targetPath = Path.Combine(profileDir, $"{SafeFileName(targetKey)}.json");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    return CodexActionResult.Fail("quota.codex_reauth_prepare_failed", $"Unable to prepare the Codex profile store: {ex.Message}");
                }
            }

            var now = DateTimeOffset.UtcNow;
            lock (QuotaReauthLock)
            {
                PruneQuotaReauthSessions(now);
                var existing = QuotaReauthSessions.Values.FirstOrDefault(session =>
                    ProfileMatches(session.AccountId, session.Email, targetAccountId, targetEmail));
                if (existing != null)
                {
                    return BuildQuotaReauthPending(existing);
                }

                var started = CodexOAuthReauthService.Start();
                if (!started.Success || string.IsNullOrWhiteSpace(started.SessionId))
                {
                    return CodexActionResult.Fail(
                        started.Code ?? "quota.codex_reauth_launch_failed",
                        started.Message ?? "Unable to open Codex authorization.");
                }

                var session = new CodexQuotaReauthSession
                {
                    SessionId = started.SessionId,
                    AccountId = targetAccountId,
                    Email = targetEmail,
                    ProfilePath = targetPath,
                    ExpiresAt = started.ExpiresAt ?? now.Add(QuotaReauthLifetime)
                };
                QuotaReauthSessions[session.SessionId] = session;
                return BuildQuotaReauthPending(session);
            }
        }

        /// <summary>
        /// Polls one in-process OAuth flow. Once OpenAI returns tokens, verifies that
        /// the signed-in identity matches the selected profile before atomically
        /// replacing only that stored profile.
        /// </summary>
        internal static CodexActionResult PollQuotaReAuth(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return CodexActionResult.Fail("quota.codex_reauth_session_required", "Authorization session is required.");
            }

            lock (QuotaReauthLock)
            {
                if (!QuotaReauthSessions.TryGetValue(sessionId, out var session))
                {
                    return CodexActionResult.Fail("quota.codex_reauth_session_not_found", "Codex authorization session was not found.");
                }

                if (DateTimeOffset.UtcNow >= session.ExpiresAt)
                {
                    QuotaReauthSessions.Remove(sessionId);
                    CodexOAuthReauthService.Forget(sessionId);
                    return CodexActionResult.Fail("quota.codex_reauth_expired", "Codex authorization expired. Start it again.");
                }

                var oauth = CodexOAuthReauthService.Poll(sessionId);
                if (string.Equals(oauth.State, "pending", StringComparison.OrdinalIgnoreCase))
                {
                    return BuildQuotaReauthPending(session);
                }

                if (!string.Equals(oauth.State, "complete", StringComparison.OrdinalIgnoreCase) || oauth.Tokens == null)
                {
                    QuotaReauthSessions.Remove(sessionId);
                    CodexOAuthReauthService.Forget(sessionId);
                    return CodexActionResult.Fail(
                        oauth.Code ?? "quota.codex_reauth_failed",
                        oauth.Message ?? "Codex authorization could not be completed.");
                }

                var (authorizedId, authorizedEmail, _) = CodexQuotaReader.ReadTokenIdentity(
                    oauth.Tokens.IdToken,
                    oauth.Tokens.AccessToken);
                if (string.IsNullOrWhiteSpace(authorizedId) && string.IsNullOrWhiteSpace(authorizedEmail))
                {
                    QuotaReauthSessions.Remove(sessionId);
                    CodexOAuthReauthService.Forget(sessionId);
                    return CodexActionResult.Fail(
                        "quota.codex_reauth_identity_missing",
                        "The authorized Codex credentials did not contain an account identity.");
                }

                if (!ProfileMatches(authorizedId, authorizedEmail, session.AccountId, session.Email))
                {
                    QuotaReauthSessions.Remove(sessionId);
                    CodexOAuthReauthService.Forget(sessionId);
                    return new CodexActionResult
                    {
                        Success = false,
                        Code = "quota.codex_reauth_account_mismatch",
                        AccountId = authorizedId,
                        Email = authorizedEmail,
                        State = "failed",
                        Message = string.IsNullOrWhiteSpace(authorizedEmail)
                            ? "The authorized Codex account does not match the selected quota account."
                            : $"Authorized {authorizedEmail}, which does not match the selected quota account."
                    };
                }

                try
                {
                    lock (ProfileFileLock)
                    {
                        WriteOAuthProfile(
                            session.ProfilePath,
                            oauth.Tokens,
                            authorizedId);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
                {
                    QuotaReauthSessions.Remove(sessionId);
                    CodexOAuthReauthService.Forget(sessionId);
                    return CodexActionResult.Fail("quota.codex_write_failed", $"Unable to update the stored Codex profile: {ex.Message}");
                }

                QuotaReauthSessions.Remove(sessionId);
                CodexOAuthReauthService.Forget(sessionId);
                return new CodexActionResult
                {
                    Success = true,
                    AccountId = authorizedId,
                    Email = authorizedEmail,
                    SessionId = session.SessionId,
                    State = "complete",
                    Message = "Codex quota authorization was updated without switching the active Codex account."
                };
            }
        }

        /// <summary>
        /// Switches the active Codex account: stop running Codex CLI, capture the
        /// current account, swap the chosen profile into ~/.codex/auth.json, relaunch.
        /// </summary>
        internal static CodexActionResult SwitchTo(string accountId, string email, bool relaunch = true)
        {
            var profile = FindProfile(accountId, email);
            if (profile == null)
            {
                return CodexActionResult.Fail("quota.codex_profile_not_found", "The selected Codex profile was not found.");
            }

            // Kill first so no live session refreshes/clobbers auth.json during the swap.
            CliProcessManager.KillByNames(CliProcessManager.CodexProcessNames);

            // Preserve whatever is currently active before we overwrite it.
            CaptureCurrent();
            profile = FindProfile(accountId, email);
            if (profile == null)
            {
                return CodexActionResult.Fail("quota.codex_profile_not_found", "The selected Codex profile was not found.");
            }

            var authFile = CodexAuthFile();
            try
            {
                Directory.CreateDirectory(CodexHome());
                if (File.Exists(authFile))
                {
                    File.Copy(authFile, authFile + ".bak", overwrite: true);
                }

                CopyAtomic(profile.Path, authFile);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return CodexActionResult.Fail("quota.codex_write_failed", $"Unable to update Codex auth.json: {ex.Message}");
            }

            if (relaunch)
            {
                if (!LaunchCodex("codex"))
                {
                    return CodexActionResult.Fail("quota.codex_launch_failed", "The account was switched, but Codex could not be reopened.");
                }
            }

            return new CodexActionResult
            {
                Success = true,
                AccountId = profile.AccountId,
                Email = profile.Email,
                Message = string.IsNullOrWhiteSpace(profile.Email)
                    ? "Codex account switched and reopened."
                    : $"Switched to {profile.Email} and reopened Codex."
            };
        }

        internal static CodexActionResult DeleteProfile(string accountId, string email)
        {
            var deleted = 0;
            foreach (var file in FindMatchingProfiles(accountId, email))
            {
                TryDeleteFile(file, ref deleted);
            }

            return new CodexActionResult
            {
                Success = deleted > 0,
                AccountId = accountId,
                Email = email,
                Affected = deleted,
                Message = deleted > 0
                    ? $"Deleted {deleted} stored Codex profile(s)."
                    : "No matching Codex profile was found.",
                Code = deleted > 0 ? null : "quota.codex_profile_not_found"
            };
        }

        private static CodexProfile FindProfile(string accountId, string email)
        {
            return ReadProfiles().FirstOrDefault(p => ProfileMatches(p.AccountId, p.Email, accountId, email));
        }

        private static IEnumerable<string> FindMatchingProfiles(string accountId, string email)
        {
            foreach (var file in FindJsonFiles(CodexProfileDirectory()))
            {
                var (storedId, storedEmail, _) = CodexQuotaReader.ReadAuthFileIdentity(file);
                if (ProfileMatches(storedId, storedEmail, accountId, email))
                {
                    yield return file;
                }
            }
        }

        internal static bool ProfileMatches(string storedId, string storedEmail, string requestedId, string requestedEmail)
        {
            // Prefer user-level email when both sides have one. ChatGPT workspace
            // account ids can be shared by multiple members with separate quotas.
            if (!string.IsNullOrWhiteSpace(requestedEmail) && !string.IsNullOrWhiteSpace(storedEmail))
            {
                return string.Equals(storedEmail, requestedEmail, StringComparison.OrdinalIgnoreCase);
            }

            return !string.IsNullOrWhiteSpace(requestedId) &&
                string.Equals(storedId, requestedId, StringComparison.OrdinalIgnoreCase);
        }

        internal static void CopyAtomic(string source, string destination)
        {
            var tmp = destination + ".tmp";
            File.Copy(source, tmp, overwrite: true);
            File.Move(tmp, destination, overwrite: true);
        }

        private static bool LaunchCodex(string command)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k {command}",
                    UseShellExecute = true,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                });
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        private static CodexActionResult BuildQuotaReauthPending(CodexQuotaReauthSession session)
        {
            return new CodexActionResult
            {
                Success = true,
                AccountId = session.AccountId,
                Email = session.Email,
                SessionId = session.SessionId,
                State = "pending",
                ExpiresAt = session.ExpiresAt,
                Message = "Complete Codex authorization in the opened browser window. The active Codex account will not be switched."
            };
        }

        private static void WriteOAuthProfile(
            string destination,
            CodexOAuthReauthService.OAuthTokens oauthTokens,
            string accountId)
        {
            if (string.IsNullOrWhiteSpace(destination) || oauthTokens == null)
            {
                throw new IOException("Codex profile destination or OAuth credentials are missing.");
            }

            var existingJson = File.Exists(destination) ? File.ReadAllText(destination) : null;
            var mergedJson = MergeOAuthProfileJson(existingJson, oauthTokens, accountId, DateTimeOffset.UtcNow);

            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, mergedJson);
            File.Move(temporary, destination, overwrite: true);
        }

        internal static string MergeOAuthProfileJson(
            string existingJson,
            CodexOAuthReauthService.OAuthTokens oauthTokens,
            string accountId,
            DateTimeOffset refreshedAt)
        {
            var root = string.IsNullOrWhiteSpace(existingJson)
                ? new JsonObject()
                : JsonNode.Parse(existingJson) as JsonObject
                    ?? throw new JsonException("Stored Codex profile is not a JSON object.");
            root["auth_mode"] = "chatgpt";
            root["OPENAI_API_KEY"] = null;
            var tokens = root["tokens"] as JsonObject;
            if (tokens == null)
            {
                tokens = new JsonObject();
                root["tokens"] = tokens;
            }
            tokens["id_token"] = oauthTokens.IdToken;
            tokens["access_token"] = oauthTokens.AccessToken;
            tokens["refresh_token"] = oauthTokens.RefreshToken;
            tokens["account_id"] = accountId;
            root["last_refresh"] = refreshedAt.ToUniversalTime().ToString("O");
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        private static void PruneQuotaReauthSessions(DateTimeOffset now)
        {
            foreach (var session in QuotaReauthSessions.Values
                .Where(item => item.ExpiresAt <= now)
                .ToList())
            {
                QuotaReauthSessions.Remove(session.SessionId);
                CodexOAuthReauthService.Forget(session.SessionId);
            }
        }
    }
}
