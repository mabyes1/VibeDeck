using System;
using System.Collections.Generic;
using System.Linq;

namespace VibeDeck.Host.Security
{
    internal static class DeviceIdentityPolicy
    {
        internal static string ResolveName(string requestedName, string model, string userAgent)
        {
            var name = string.IsNullOrWhiteSpace(requestedName) ? "" : requestedName.Trim();
            var normalizedModel = NormalizeModel(model);
            if (!string.IsNullOrWhiteSpace(normalizedModel))
            {
                if (normalizedModel.Equals("GoColor7", StringComparison.OrdinalIgnoreCase) ||
                    normalizedModel.Replace(" ", "").Equals("BOOXGoColor7", StringComparison.OrdinalIgnoreCase))
                {
                    return "BOOX Go Color 7";
                }

                if (normalizedModel.StartsWith("SM-", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Samsung {normalizedModel.ToUpperInvariant()}";
                }

                return normalizedModel;
            }

            if (!string.IsNullOrWhiteSpace(name) && !IsGenericPairingName(name))
            {
                return name;
            }

            var agent = userAgent ?? "";
            if (agent.IndexOf("Android", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Android Phone";
            }

            if (agent.IndexOf("iPhone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                agent.IndexOf("iPad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                agent.IndexOf("iPod", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "iPhone";
            }

            return string.IsNullOrWhiteSpace(name) ? "Phone" : name;
        }

        internal static string NormalizeModel(string model)
        {
            var value = string.IsNullOrWhiteSpace(model) ? "" : model.Trim();
            if (value.Length > 80 || value.Equals("K", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }
            return value;
        }

        internal static string NormalizeClientInstanceId(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
            return normalized.Length >= 16 && normalized.Length <= 100 ? normalized : "";
        }

        internal static bool ApplyIdentity(
            List<TrustedDeviceRecord> records,
            TrustedDeviceRecord device,
            string model,
            string clientInstanceId,
            string userAgent)
        {
            var normalizedModel = NormalizeModel(model);
            var normalizedClientId = NormalizeClientInstanceId(clientInstanceId);
            var changed = false;

            if (!string.IsNullOrWhiteSpace(normalizedModel) &&
                !string.Equals(device.Model, normalizedModel, StringComparison.Ordinal))
            {
                device.Model = normalizedModel;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(normalizedClientId) &&
                !string.Equals(device.ClientInstanceId, normalizedClientId, StringComparison.Ordinal))
            {
                device.ClientInstanceId = normalizedClientId;
                changed = true;
            }

            var resolvedName = ResolveName(device.Name, device.Model, userAgent);
            if (!string.Equals(device.Name, resolvedName, StringComparison.Ordinal))
            {
                device.Name = resolvedName;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(device.ClientInstanceId))
            {
                changed |= records.RemoveAll(item =>
                    !ReferenceEquals(item, device) &&
                    string.Equals(item.ClientInstanceId, device.ClientInstanceId, StringComparison.Ordinal)) > 0;
            }

            return changed;
        }

        internal static TrustedDeviceRecord FindPairingContinuation(
            IEnumerable<TrustedDeviceRecord> records,
            PendingApprovalPairing request)
        {
            if (!string.IsNullOrWhiteSpace(request.ClientInstanceId))
            {
                var stable = records
                    .Where(item => string.Equals(item.ClientInstanceId, request.ClientInstanceId, StringComparison.Ordinal))
                    .OrderByDescending(item => item.LastSeenAt)
                    .FirstOrDefault();
                if (stable != null)
                {
                    return stable;
                }
            }

            return records
                .Where(item =>
                    string.IsNullOrWhiteSpace(item.ClientInstanceId) &&
                    string.Equals(item.LastRemoteAddress, request.RemoteAddress, StringComparison.Ordinal) &&
                    string.Equals(item.LastUserAgent, request.UserAgent, StringComparison.Ordinal))
                .OrderByDescending(item => item.LastSeenAt)
                .FirstOrDefault();
        }

        internal static bool NormalizeRecords(List<TrustedDeviceRecord> records)
        {
            var changed = false;
            foreach (var record in records)
            {
                var resolvedName = ResolveName(record.Name, record.Model, record.LastUserAgent);
                if (!string.Equals(record.Name, resolvedName, StringComparison.Ordinal))
                {
                    record.Name = resolvedName;
                    changed = true;
                }
            }

            foreach (var group in records
                .Where(record => !string.IsNullOrWhiteSpace(record.ClientInstanceId))
                .GroupBy(record => record.ClientInstanceId, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .ToList())
            {
                var keep = group.OrderByDescending(record => record.LastSeenAt).First();
                changed |= records.RemoveAll(record => group.Contains(record) && !ReferenceEquals(record, keep)) > 0;
            }

            foreach (var group in records
                .Where(record => string.IsNullOrWhiteSpace(record.ClientInstanceId) &&
                    !string.IsNullOrWhiteSpace(record.LastRemoteAddress) &&
                    !string.IsNullOrWhiteSpace(record.LastUserAgent))
                .GroupBy(record => $"{record.LastRemoteAddress}\n{record.LastUserAgent}", StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .ToList())
            {
                var keep = group.OrderByDescending(record => record.LastSeenAt).First();
                changed |= records.RemoveAll(record => group.Contains(record) && !ReferenceEquals(record, keep)) > 0;
            }

            return changed;
        }

        private static bool IsGenericPairingName(string name)
        {
            return string.Equals(name, "Phone", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Win32", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Android Phone", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Android 裝置", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "iPhone", StringComparison.OrdinalIgnoreCase);
        }
    }
}
