using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace VibeDeck.Host.Security
{
    internal sealed class PairingApprovalRegistry
    {
        private readonly Dictionary<string, PendingApprovalPairing> requests =
            new Dictionary<string, PendingApprovalPairing>(StringComparer.Ordinal);

        internal PairingApprovalRequestResult Request(
            string name,
            string platform,
            string model,
            string clientInstanceId,
            string userAgent,
            string remoteAddress,
            DateTimeOffset now)
        {
            RemoveExpired(now);
            var normalizedClientId = DeviceIdentityPolicy.NormalizeClientInstanceId(clientInstanceId);
            var existing = requests.Values.FirstOrDefault(item =>
                item.ExpiresAt > now &&
                item.Status == "pending" &&
                ((!string.IsNullOrWhiteSpace(normalizedClientId) &&
                  string.Equals(item.ClientInstanceId, normalizedClientId, StringComparison.Ordinal)) ||
                 (string.IsNullOrWhiteSpace(normalizedClientId) &&
                  string.Equals(item.RemoteAddress, remoteAddress, StringComparison.Ordinal) &&
                  string.Equals(item.UserAgent, userAgent, StringComparison.Ordinal))));
            if (existing != null)
            {
                return PairingApprovalRequestResult.From(existing, existing.RequestSecret);
            }

            var secret = CreateOpaqueToken(32);
            var request = new PendingApprovalPairing
            {
                RequestId = CreateOpaqueToken(18),
                RequestSecretHash = HashToken(secret),
                RequestSecret = secret,
                Name = DeviceIdentityPolicy.ResolveName(name, model, userAgent),
                Platform = string.IsNullOrWhiteSpace(platform) ? "web" : platform.Trim(),
                Model = DeviceIdentityPolicy.NormalizeModel(model),
                ClientInstanceId = normalizedClientId,
                RemoteAddress = remoteAddress,
                UserAgent = userAgent,
                VerificationCode = CreateNumericCode(),
                Status = "pending",
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(10)
            };
            requests[request.RequestId] = request;
            return PairingApprovalRequestResult.From(request, secret);
        }

        internal List<PairingApprovalSummary> GetPending(DateTimeOffset now)
        {
            RemoveExpired(now);
            return requests.Values
                .Where(item => item.Status == "pending")
                .OrderBy(item => item.CreatedAt)
                .Select(PairingApprovalSummary.From)
                .ToList();
        }

        internal bool TryGetPending(string requestId, DateTimeOffset now, out PendingApprovalPairing request)
        {
            RemoveExpired(now);
            return requests.TryGetValue(requestId ?? "", out request) && request.Status == "pending";
        }

        internal DeviceTrustActionResult Deny(string requestId)
        {
            if (!requests.TryGetValue(requestId ?? "", out var request))
            {
                return DeviceTrustActionResult.Fail("Pairing request expired or was not found.");
            }

            request.Status = "denied";
            return new DeviceTrustActionResult { Success = true, Message = "Pairing request denied." };
        }

        internal PairingApprovalPollResult Poll(string requestId, string requestSecret, DateTimeOffset now)
        {
            RemoveExpired(now);
            if (!requests.TryGetValue(requestId ?? "", out var request) ||
                !string.Equals(request.RequestSecretHash, HashToken(requestSecret), StringComparison.Ordinal))
            {
                return PairingApprovalPollResult.Fail("Pairing request expired or invalid.");
            }

            var result = new PairingApprovalPollResult
            {
                Success = true,
                Status = request.Status,
                DeviceId = request.Status == "approved" ? request.DeviceId : null,
                DeviceToken = request.Status == "approved" ? request.DeviceToken : null,
                DeviceName = request.Name,
                Continued = request.Continued
            };
            if (request.Status == "approved" || request.Status == "denied")
            {
                requests.Remove(request.RequestId);
            }
            return result;
        }

        internal void MarkApproved(PendingApprovalPairing request, string deviceId, string deviceToken, bool continued)
        {
            request.Status = "approved";
            request.DeviceId = deviceId;
            request.DeviceToken = deviceToken;
            request.Continued = continued;
        }

        internal void Clear()
        {
            requests.Clear();
        }

        internal static string CreateOpaqueToken(int byteCount)
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

        private void RemoveExpired(DateTimeOffset now)
        {
            foreach (var expired in requests.Where(item => item.Value.ExpiresAt <= now).Select(item => item.Key).ToList())
            {
                requests.Remove(expired);
            }
        }

        private static string CreateNumericCode()
        {
            var bytes = new byte[4];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return (BitConverter.ToUInt32(bytes, 0) % 1000000).ToString("D6");
        }

        private static string HashToken(string token)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        }
    }
}
