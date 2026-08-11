using System;
using System.Security.Cryptography;

namespace VibeDeck.Host.Security
{
    public sealed class ActionTokenService
    {
        public const string HeaderName = "X-VibeDeck-Action-Token";
        private readonly string token;

        public ActionTokenService()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            token = Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public string Token => token;

        public bool IsValid(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                string.Equals(value, token, StringComparison.Ordinal);
        }
    }
}
