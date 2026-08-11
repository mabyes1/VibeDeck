using System;
using System.Text.Json;

namespace VibeDeck.Host.Updates
{
    public sealed class ProductUpdateStatus
    {
        public string State { get; set; }
        public string Code { get; set; }
        public string CurrentVersion { get; set; }
        public string LatestVersion { get; set; }
        public string ReleaseUrl { get; set; }
        public bool UpdateAvailable { get; set; }
        public bool CanStart { get; set; }
        public int DownloadPercent { get; set; }

        public ProductUpdateStatus Copy()
        {
            return new ProductUpdateStatus
            {
                State = State,
                Code = Code,
                CurrentVersion = CurrentVersion,
                LatestVersion = LatestVersion,
                ReleaseUrl = ReleaseUrl,
                UpdateAvailable = UpdateAvailable,
                CanStart = CanStart,
                DownloadPercent = DownloadPercent
            };
        }
    }

    public sealed class ProductUpdateRelease
    {
        private const string RepositoryPath = "/mabyes1/VibeDeck/releases/download/";
        private const string RepositoryReleasePage = "https://github.com/mabyes1/VibeDeck/releases";

        public string Version { get; private set; }
        public string InstallerFileName { get; private set; }
        public string ReleaseUrl { get; private set; }
        public Uri InstallerUrl { get; private set; }
        public Uri ChecksumUrl { get; private set; }

        /// <summary>
        /// Optional detached release signature asset (VibeDeck-Setup-x.y.z.exe.sig).
        /// Null when the release does not publish one; enforcement of its presence
        /// is decided by ProductUpdateOptions.RequireSignedUpdates.
        /// </summary>
        public Uri SignatureUrl { get; private set; }

        public bool UpdateAvailable { get; private set; }

        public static bool TryParse(
            JsonElement releaseJson,
            string currentVersion,
            out ProductUpdateRelease release,
            out string errorCode)
        {
            release = null;
            errorCode = "release_invalid";
            if (releaseJson.ValueKind != JsonValueKind.Object ||
                GetBoolean(releaseJson, "draft") ||
                GetBoolean(releaseJson, "prerelease"))
            {
                return false;
            }

            if (!ProductVersion.TryNormalize(GetString(releaseJson, "tag_name"), out var version))
            {
                return false;
            }

            var candidate = new ProductUpdateRelease
            {
                Version = version,
                InstallerFileName = $"VibeDeck-Setup-{version}.exe",
                ReleaseUrl = TrustedReleasePage(GetString(releaseJson, "html_url")),
                UpdateAvailable = ProductVersion.IsNewer(version, currentVersion)
            };

            if (!candidate.UpdateAvailable)
            {
                release = candidate;
                errorCode = "";
                return true;
            }

            if (!releaseJson.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var installer = FindAsset(assets, candidate.InstallerFileName);
            var checksum = FindAsset(assets, candidate.InstallerFileName + ".sha256");
            if (installer == null || checksum == null ||
                !TryTrustedAssetUri(installer, out var installerUri) ||
                !TryTrustedAssetUri(checksum, out var checksumUri))
            {
                return false;
            }

            candidate.InstallerUrl = installerUri;
            candidate.ChecksumUrl = checksumUri;

            var signature = FindAsset(assets, candidate.InstallerFileName + ".sig");
            if (!string.IsNullOrEmpty(signature))
            {
                // A published .sig asset from an untrusted origin means the release
                // metadata is not to be trusted at all.
                if (!TryTrustedAssetUri(signature, out var signatureUri))
                {
                    return false;
                }

                candidate.SignatureUrl = signatureUri;
            }

            release = candidate;
            errorCode = "";
            return true;
        }

        private static string FindAsset(JsonElement assets, string expectedName)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object ||
                    !string.Equals(GetString(asset, "name"), expectedName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return GetString(asset, "browser_download_url");
            }

            return "";
        }

        private static bool TryTrustedAssetUri(string value, out Uri uri)
        {
            uri = null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
                !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(parsed.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                !parsed.AbsolutePath.StartsWith(RepositoryPath, StringComparison.Ordinal))
            {
                return false;
            }

            uri = parsed;
            return true;
        }

        private static string TrustedReleasePage(string value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
                string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(parsed.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
                parsed.AbsolutePath.StartsWith("/mabyes1/VibeDeck/releases/", StringComparison.Ordinal))
            {
                return parsed.ToString();
            }

            return RepositoryReleasePage;
        }

        private static string GetString(JsonElement source, string propertyName)
        {
            return source.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }

        private static bool GetBoolean(JsonElement source, string propertyName)
        {
            return source.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
        }
    }
}
