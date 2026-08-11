using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static VibeDeck.Host.Quotas.QuotaJsonHelpers;

namespace VibeDeck.Host.Quotas
{
    /// <summary>
    /// Windows DPAPI (current-user) protection for AGY refresh tokens.
    /// Extracted from AiQuotaService (refactor/quota-split step 1). No behavior change.
    /// </summary>
    internal static class SecretProtector
    {
        internal static readonly byte[] AgyTokenEntropy = Encoding.UTF8.GetBytes("VibeDeck.AGY.RefreshToken.v1");

        internal static string ProtectSecret(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            var protectedBytes = ProtectedData.Protect(bytes, AgyTokenEntropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        internal static string ReadProtectedSecret(JsonElement root, string propertyName)
        {
            var protectedValue = TryGetString(root, propertyName);
            if (string.IsNullOrWhiteSpace(protectedValue))
            {
                return null;
            }

            try
            {
                var protectedBytes = Convert.FromBase64String(protectedValue);
                var bytes = ProtectedData.Unprotect(protectedBytes, AgyTokenEntropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
            {
                return null;
            }
        }
    }
}
