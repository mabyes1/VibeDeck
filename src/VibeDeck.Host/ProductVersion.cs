using System;

namespace VibeDeck.Host
{
    public static class ProductVersion
    {
        private static readonly Lazy<string> CurrentLazy = new Lazy<string>(ResolveCurrent);

        public static string Current => CurrentLazy.Value;

        public static bool TryNormalize(string value, out string normalized)
        {
            normalized = "";
            var candidate = (value ?? "").Trim().TrimStart('v', 'V');
            if (!Version.TryParse(candidate, out var parsed) || parsed.Major < 0 || parsed.Minor < 0)
            {
                return false;
            }

            var build = Math.Max(parsed.Build, 0);
            normalized = $"{parsed.Major}.{parsed.Minor}.{build}";
            return true;
        }

        public static bool IsNewer(string candidate, string current)
        {
            if (!TryNormalize(candidate, out var normalizedCandidate) ||
                !TryNormalize(current, out var normalizedCurrent) ||
                !Version.TryParse(normalizedCandidate, out var candidateVersion) ||
                !Version.TryParse(normalizedCurrent, out var currentVersion))
            {
                return false;
            }

            return candidateVersion > currentVersion;
        }

        private static string ResolveCurrent()
        {
            try
            {
                var version = typeof(ProductVersion).Assembly.GetName().Version;
                if (version != null)
                {
                    return $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
                }
            }
            catch
            {
            }

            return "0.0.0";
        }
    }
}
