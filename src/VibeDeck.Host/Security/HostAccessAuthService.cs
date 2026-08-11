using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace VibeDeck.Host.Security
{
    public sealed class HostAccessAuthService
    {
        public const string CookieName = "VibeDeck-Host-Session";

        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 120000;
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);
        private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);
        private readonly object sync = new object();
        private readonly string passwordHash;
        private readonly ConcurrentDictionary<string, AuthSession> sessions = new ConcurrentDictionary<string, AuthSession>(StringComparer.Ordinal);
        private readonly Dictionary<string, FailureRecord> failures = new Dictionary<string, FailureRecord>(StringComparer.Ordinal);

        public HostAccessAuthService(IConfiguration configuration)
        {
            var configuredHash = configuration["RemoteAccess:PasswordHash"];
            var configuredPassword = configuration["RemoteAccess:Password"];
            if (string.IsNullOrWhiteSpace(configuredPassword))
            {
                configuredPassword = Environment.GetEnvironmentVariable("VIBEDECK_REMOTE_PASSWORD");
            }

            passwordHash = !string.IsNullOrWhiteSpace(configuredHash)
                ? configuredHash.Trim()
                : CreatePasswordHash(configuredPassword);
        }

        public bool Enabled => !string.IsNullOrWhiteSpace(passwordHash);

        public bool IsAuthenticated(HttpContext context)
        {
            if (!Enabled)
            {
                return false;
            }

            var token = ReadSessionToken(context);
            if (string.IsNullOrWhiteSpace(token) || !sessions.TryGetValue(HashToken(token), out var session))
            {
                return false;
            }

            if (session.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                sessions.TryRemove(HashToken(token), out _);
                return false;
            }

            return true;
        }

        private static string ReadSessionToken(HttpContext context)
        {
            return context.Request.Cookies[CookieName];
        }

        public HostLoginResult Login(string password, string remoteAddress)
        {
            if (!Enabled)
            {
                return HostLoginResult.Fail("遠端密碼登入尚未啟用。", "auth.password_not_configured");
            }

            var address = string.IsNullOrWhiteSpace(remoteAddress) ? "unknown" : remoteAddress;
            if (IsLockedOut(address))
            {
                return HostLoginResult.Fail("登入嘗試過多，請稍後再試。", "auth.rate_limited");
            }

            if (!VerifyPassword(password, passwordHash))
            {
                RegisterFailure(address);
                return HostLoginResult.Fail("密碼不正確。", "auth.invalid_password");
            }

            lock (sync)
            {
                failures.Remove(address);
            }

            var token = CreateToken(32);
            sessions[HashToken(token)] = new AuthSession
            {
                ExpiresAt = DateTimeOffset.UtcNow.Add(SessionLifetime)
            };
            PruneSessions();
            return HostLoginResult.Ok(token, SessionLifetime);
        }

        public void Logout(HttpContext context)
        {
            var token = ReadSessionToken(context);
            if (!string.IsNullOrWhiteSpace(token))
            {
                sessions.TryRemove(HashToken(token), out _);
            }
        }

        private bool IsLockedOut(string address)
        {
            lock (sync)
            {
                if (!failures.TryGetValue(address, out var failure))
                {
                    return false;
                }

                var now = DateTimeOffset.UtcNow;
                if (failure.LockedUntil > now)
                {
                    return true;
                }

                if (now - failure.FirstFailureAt > FailureWindow)
                {
                    failures.Remove(address);
                }

                return false;
            }
        }

        private void RegisterFailure(string address)
        {
            lock (sync)
            {
                var now = DateTimeOffset.UtcNow;
                if (!failures.TryGetValue(address, out var failure) || now - failure.FirstFailureAt > FailureWindow)
                {
                    failure = new FailureRecord { FirstFailureAt = now };
                    failures[address] = failure;
                }

                failure.Count++;
                if (failure.Count >= 5)
                {
                    failure.LockedUntil = now.Add(LockoutDuration);
                }
            }
        }

        private void PruneSessions()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var item in sessions.Where(item => item.Value.ExpiresAt <= now).ToList())
            {
                sessions.TryRemove(item.Key, out _);
            }
        }

        private static string CreatePasswordHash(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return null;
            }

            var salt = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            var hash = Derive(password, salt, Iterations);
            return $"v1:{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
        }

        private static bool VerifyPassword(string password, string encoded)
        {
            try
            {
                if (string.IsNullOrEmpty(password))
                {
                    return false;
                }

                var parts = encoded.Split(':');
                if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations))
                {
                    return false;
                }

                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                var actual = Derive(password, salt, iterations);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static byte[] Derive(string password, byte[] salt, int iterations)
        {
            using (var derive = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
            {
                return derive.GetBytes(HashSize);
            }
        }

        private static string HashToken(string token)
        {
            using (var sha = SHA256.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(token ?? string.Empty)));
            }
        }

        private static string CreateToken(int byteCount)
        {
            var bytes = new byte[byteCount];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private sealed class AuthSession
        {
            public DateTimeOffset ExpiresAt { get; set; }
        }

        private sealed class FailureRecord
        {
            public DateTimeOffset FirstFailureAt { get; set; }
            public int Count { get; set; }
            public DateTimeOffset LockedUntil { get; set; }
        }
    }

    public sealed class HostLoginResult
    {
        public bool Success { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public string SessionToken { get; set; }
        public TimeSpan SessionLifetime { get; set; }

        public static HostLoginResult Ok(string token, TimeSpan lifetime) => new HostLoginResult
        {
            Success = true,
            Code = "auth.success",
            Message = "登入成功。",
            SessionToken = token,
            SessionLifetime = lifetime
        };

        public static HostLoginResult Fail(string message, string code) => new HostLoginResult
        {
            Success = false,
            Code = code,
            Message = message
        };
    }

    public sealed class HostLoginRequest
    {
        public string Password { get; set; }
    }
}
