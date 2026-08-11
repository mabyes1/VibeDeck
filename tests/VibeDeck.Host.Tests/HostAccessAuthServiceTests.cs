using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using VibeDeck.Host.Security;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class HostAccessAuthServiceTests
    {
        private const string Password = "correct-horse-battery-staple";

        private static HostAccessAuthService Create(params (string Key, string Value)[] settings)
        {
            var values = new Dictionary<string, string>();
            foreach (var (key, value) in settings)
            {
                values[key] = value;
            }

            return new HostAccessAuthService(
                new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        }

        [Fact]
        public void RemoteLoginIsDisabledUntilAPasswordIsConfigured()
        {
            var auth = Create();

            Assert.False(auth.Enabled);
            var result = auth.Login(Password, "203.0.113.7");
            Assert.False(result.Success);
            Assert.Equal("auth.password_not_configured", result.Code);
        }

        [Fact]
        public void CorrectPasswordIssuesASessionToken()
        {
            var auth = Create(("RemoteAccess:Password", Password));

            var result = auth.Login(Password, "203.0.113.7");

            Assert.True(auth.Enabled);
            Assert.True(result.Success);
            Assert.False(string.IsNullOrWhiteSpace(result.SessionToken));
        }

        [Theory]
        [InlineData("wrong")]
        [InlineData("")]
        [InlineData(null)]
        public void WrongOrEmptyPasswordIsRejected(string attempt)
        {
            var auth = Create(("RemoteAccess:Password", Password));

            var result = auth.Login(attempt, "203.0.113.7");

            Assert.False(result.Success);
            Assert.Equal("auth.invalid_password", result.Code);
        }

        [Fact]
        public void FiveFailuresLockTheAddressOutEvenWithTheCorrectPassword()
        {
            var auth = Create(("RemoteAccess:Password", Password));
            const string address = "203.0.113.7";

            for (var attempt = 0; attempt < 5; attempt++)
            {
                Assert.Equal("auth.invalid_password", auth.Login("wrong", address).Code);
            }

            // The lockout must hold against the real password too, otherwise it only slows a
            // guesser down until the moment they succeed.
            var afterLockout = auth.Login(Password, address);
            Assert.False(afterLockout.Success);
            Assert.Equal("auth.rate_limited", afterLockout.Code);
        }

        [Fact]
        public void SuccessfulLoginClearsEarlierFailures()
        {
            var auth = Create(("RemoteAccess:Password", Password));
            const string address = "203.0.113.7";

            for (var attempt = 0; attempt < 4; attempt++)
            {
                auth.Login("wrong", address);
            }
            Assert.True(auth.Login(Password, address).Success);

            // The counter reset, so a fresh run of failures is needed to lock out again.
            for (var attempt = 0; attempt < 4; attempt++)
            {
                Assert.Equal("auth.invalid_password", auth.Login("wrong", address).Code);
            }
            Assert.True(auth.Login(Password, address).Success);
        }

        [Fact]
        public void LockoutIsScopedPerAddressOnly()
        {
            // Documents a real limitation: the lockout is keyed on source address, so an
            // attacker rotating IPs gets a fresh budget each time. The global bucket in
            // RequestRateLimiter (see Startup.RateLimiting) is what bounds that case.
            var auth = Create(("RemoteAccess:Password", Password));

            for (var attempt = 0; attempt < 5; attempt++)
            {
                auth.Login("wrong", "203.0.113.7");
            }
            Assert.Equal("auth.rate_limited", auth.Login(Password, "203.0.113.7").Code);

            Assert.Equal("auth.invalid_password", auth.Login("wrong", "203.0.113.8").Code);
        }

        [Fact]
        public void UnknownAddressesShareTheSameBudgetRatherThanBypassingTheLockout()
        {
            var auth = Create(("RemoteAccess:Password", Password));

            for (var attempt = 0; attempt < 5; attempt++)
            {
                auth.Login("wrong", null);
            }

            Assert.Equal("auth.rate_limited", auth.Login(Password, "").Code);
        }

        [Fact]
        public void PreHashedPasswordFromConfigurationIsAccepted()
        {
            // The deployed form: operators configure a PBKDF2 hash, never a plaintext password.
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            byte[] expected;
            using (var derive = new Rfc2898DeriveBytes(Password, salt, 120000, HashAlgorithmName.SHA256))
            {
                expected = derive.GetBytes(32);
            }

            var encoded = $"v1:120000:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(expected)}";
            var auth = Create(("RemoteAccess:PasswordHash", encoded));

            Assert.True(auth.Enabled);
            Assert.True(auth.Login(Password, "203.0.113.7").Success);
            Assert.False(auth.Login("wrong", "203.0.113.9").Success);
        }

        [Fact]
        public void MalformedPasswordHashNeverAuthenticates()
        {
            var auth = Create(("RemoteAccess:PasswordHash", "v1:not-a-number:zzz:zzz"));

            Assert.True(auth.Enabled);
            Assert.False(auth.Login(Password, "203.0.113.7").Success);
            Assert.False(auth.Login("", "203.0.113.7").Success);
        }
    }
}
