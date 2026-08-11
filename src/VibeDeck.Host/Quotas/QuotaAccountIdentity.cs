using System;

namespace VibeDeck.Host.Quotas
{
    internal static class QuotaAccountIdentity
    {
        internal static bool Matches(string storedAccountId, string storedEmail, string requestedAccountId, string requestedEmail)
        {
            return (!string.IsNullOrWhiteSpace(requestedAccountId) &&
                    string.Equals(storedAccountId, requestedAccountId, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(requestedEmail) &&
                    string.Equals(storedEmail, requestedEmail, StringComparison.OrdinalIgnoreCase));
        }
    }
}
