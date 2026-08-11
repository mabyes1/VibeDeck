using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VibeDeck.Host.Diagnostics;

namespace VibeDeck.Host.Updates
{
    public sealed class ProductUpdateService
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/mabyes1/VibeDeck/releases/latest";
        private static readonly Regex Sha256Pattern = new Regex(@"\b[a-fA-F0-9]{64}\b", RegexOptions.Compiled);
        private static readonly Regex Base64SignaturePattern = new Regex(@"^[A-Za-z0-9+/=\s]+$", RegexOptions.Compiled);
        private readonly AuditTrailService audit;
        private readonly ProductUpdateOptions options;
        private readonly IUpdateHttpTransport transport;
        private readonly IInstallerLauncher launcher;
        private readonly IAuthenticodeVerifier authenticodeVerifier;
        private readonly object gate = new object();
        private ProductUpdateStatus status = new ProductUpdateStatus
        {
            State = "idle",
            Code = "idle",
            CurrentVersion = ProductVersion.Current
        };
        private ProductUpdateRelease latestRelease;
        private bool operationActive;

        public ProductUpdateService(AuditTrailService audit)
            : this(audit, null, null, null, null)
        {
        }

        internal ProductUpdateService(
            AuditTrailService audit,
            ProductUpdateOptions options,
            IUpdateHttpTransport transport,
            IInstallerLauncher launcher,
            IAuthenticodeVerifier authenticodeVerifier)
        {
            this.audit = audit;
            this.options = options ?? new ProductUpdateOptions();
            this.transport = transport ?? new GitHubUpdateTransport();
            this.launcher = launcher ?? new ShellInstallerLauncher();
            this.authenticodeVerifier = authenticodeVerifier ?? new InstallerVerifier();
        }

        public ProductUpdateStatus GetStatus()
        {
            lock (gate)
            {
                return status.Copy();
            }
        }

        public async Task<ProductUpdateStatus> CheckAsync(string traceId)
        {
            if (!AppPaths.IsInstalledLayout)
            {
                return Publish("unavailable", "installed_product_required");
            }

            if (!TryBeginOperation())
            {
                return GetStatus();
            }

            try
            {
                return await CheckCoreAsync(traceId);
            }
            finally
            {
                EndOperation();
            }
        }

        public ProductUpdateStatus Start(string traceId)
        {
            if (!AppPaths.IsInstalledLayout)
            {
                return Publish("unavailable", "installed_product_required");
            }

            if (!TryBeginOperation())
            {
                return GetStatus();
            }

            var result = Publish("checking", "checking");
            _ = Task.Run(() => DownloadAndLaunchAsync(traceId));
            return result;
        }

        private async Task<ProductUpdateStatus> CheckCoreAsync(string traceId)
        {
            Publish("checking", "checking");
            try
            {
                var release = await FetchLatestReleaseAsync();
                if (!release.UpdateAvailable)
                {
                    audit.Record(
                        "information",
                        "product-update",
                        "check",
                        "current",
                        traceId,
                        details: ReleaseDetails(release));
                    return Publish("current", "current", release);
                }

                audit.Record(
                    "information",
                    "product-update",
                    "check",
                    "available",
                    traceId,
                    details: ReleaseDetails(release));
                return Publish("available", "available", release, canStart: true);
            }
            catch (ProductUpdateException error)
            {
                audit.RecordException("product-update", "check", error, traceId);
                return Publish(error.Code == "not_published" ? "unavailable" : "failed", error.Code);
            }
            catch (Exception error)
            {
                audit.RecordException("product-update", "check", error, traceId);
                return Publish("failed", "network_error");
            }
        }

        internal async Task DownloadAndLaunchAsync(string traceId)
        {
            string partialPath = null;
            string installerPath = null;
            try
            {
                var release = await FetchLatestReleaseAsync();
                if (!release.UpdateAvailable)
                {
                    Publish("current", "current", release);
                    audit.Record("information", "product-update", "start", "current", traceId, details: ReleaseDetails(release));
                    return;
                }

                Publish("downloading", "downloading", release, downloadPercent: 0);
                var expectedHash = await FetchExpectedHashAsync(release);
                var expectedSignature = await FetchDetachedSignatureAsync(release);
                var updatesDirectory = ResolveStagingDirectory();
                installerPath = Path.Combine(updatesDirectory, release.InstallerFileName);
                partialPath = installerPath + ".downloading";
                if (File.Exists(partialPath))
                {
                    File.Delete(partialPath);
                }

                await DownloadInstallerAsync(release, partialPath);
                File.Move(partialPath, installerPath, overwrite: true);
                partialPath = null;

                // TOCTOU defence: open the final file with a handle that denies
                // writers/deleters (FileShare.Read), verify the EXACT bytes behind
                // that handle, and keep the handle open until the installer has
                // been handed to the OS. The staging directory lives under the
                // user profile, so nothing may modify the file between hash /
                // signature verification and launch.
                using (var installerStream = new FileStream(
                    installerPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read))
                {
                    var actualHash = CalculateSha256(installerStream);
                    if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ProductUpdateException("checksum_mismatch");
                    }

                    VerifyDetachedSignaturePolicy(actualHash, expectedSignature, traceId, release);
                    VerifyAuthenticodePolicy(installerPath, traceId, release);

                    Publish("ready", "verified", release, downloadPercent: 100);

                    var started = launcher.Launch(
                        installerPath,
                        "/SP- /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /NORESTART",
                        updatesDirectory);
                    if (!started)
                    {
                        throw new ProductUpdateException("installer_launch_failed");
                    }
                }

                audit.Record(
                    "information",
                    "product-update",
                    "start",
                    "installer-started",
                    traceId,
                    details: ReleaseDetails(release));
                Publish("launching", "installer_started", release, downloadPercent: 100);
            }
            catch (ProductUpdateException error)
            {
                DeleteFileQuietly(partialPath);
                DeleteFileQuietly(installerPath);
                audit.RecordException("product-update", "start", error, traceId);
                Publish("failed", error.Code);
            }
            catch (Exception error)
            {
                DeleteFileQuietly(partialPath);
                DeleteFileQuietly(installerPath);
                audit.RecordException("product-update", "start", error, traceId);
                Publish("failed", "update_failed");
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// Detached release-signature policy. The same-origin .sha256 only proves
        /// integrity in transit; only the offline-key signature proves the release
        /// was produced by the project owner (defends against a compromised GitHub
        /// account/CI token publishing a malicious exe + matching checksum).
        /// </summary>
        private void VerifyDetachedSignaturePolicy(
            string installerSha256,
            string signatureBase64,
            string traceId,
            ProductUpdateRelease release)
        {
            var publicKeyConfigured = !string.IsNullOrWhiteSpace(options.ReleaseSigningPublicKeyPem);
            if (options.RequireSignedUpdates)
            {
                if (!publicKeyConfigured)
                {
                    // Enforcement is on but no key was pinned: refuse to run
                    // anything rather than silently skipping verification.
                    throw new ProductUpdateException("signature_key_missing");
                }

                if (string.IsNullOrWhiteSpace(signatureBase64))
                {
                    throw new ProductUpdateException("signature_missing");
                }

                if (!InstallerVerifier.VerifyDetachedSignature(installerSha256, signatureBase64, options.ReleaseSigningPublicKeyPem))
                {
                    throw new ProductUpdateException("signature_invalid");
                }

                return;
            }

            // Relaxed mode (backward compatibility with unsigned releases): if a
            // signature IS published and a key IS pinned, it still must verify -
            // fail closed on tamper. Absence is tolerated but audited.
            if (!string.IsNullOrWhiteSpace(signatureBase64) && publicKeyConfigured)
            {
                if (!InstallerVerifier.VerifyDetachedSignature(installerSha256, signatureBase64, options.ReleaseSigningPublicKeyPem))
                {
                    throw new ProductUpdateException("signature_invalid");
                }

                return;
            }

            audit.Record(
                "warning",
                "product-update",
                "verify",
                "unsigned-release-accepted",
                traceId,
                details: ReleaseDetails(release));
        }

        /// <summary>
        /// Authenticode policy: unsigned installers are currently tolerated
        /// (production code signing is not in place yet), but a PRESENT signature
        /// must be cryptographically valid, and when publisher thumbprints are
        /// pinned the signer must match one of them.
        /// </summary>
        private void VerifyAuthenticodePolicy(string installerPath, string traceId, ProductUpdateRelease release)
        {
            var check = authenticodeVerifier.Check(installerPath);
            switch (check.Status)
            {
                case AuthenticodeStatus.NotSigned:
                    audit.Record(
                        "warning",
                        "product-update",
                        "verify",
                        "authenticode-not-signed",
                        traceId,
                        details: ReleaseDetails(release));
                    return;
                case AuthenticodeStatus.Invalid:
                    throw new ProductUpdateException("authenticode_invalid");
                case AuthenticodeStatus.Valid:
                    var pins = options.PinnedAuthenticodeThumbprints ?? Array.Empty<string>();
                    if (pins.Length == 0)
                    {
                        return;
                    }

                    foreach (var pin in pins)
                    {
                        if (string.Equals(pin, check.SignerThumbprint, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                    }

                    throw new ProductUpdateException("authenticode_untrusted_publisher");
                default:
                    throw new ProductUpdateException("authenticode_invalid");
            }
        }

        private string ResolveStagingDirectory()
        {
            if (!string.IsNullOrWhiteSpace(options.StagingDirectoryOverride))
            {
                Directory.CreateDirectory(options.StagingDirectoryOverride);
                return options.StagingDirectoryOverride;
            }

            return AppPaths.EnsureDirectory(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppPaths.ProductName,
                "updates"));
        }

        private async Task<ProductUpdateRelease> FetchLatestReleaseAsync()
        {
            using var response = await transport.SendAsync(new Uri(LatestReleaseApi), HttpCompletionOption.ResponseContentRead);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ProductUpdateException("not_published");
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new ProductUpdateException("release_lookup_failed");
            }

            var content = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(content);
            if (!ProductUpdateRelease.TryParse(document.RootElement, ProductVersion.Current, out var release, out var errorCode))
            {
                throw new ProductUpdateException(string.IsNullOrWhiteSpace(errorCode) ? "release_invalid" : errorCode);
            }

            return release;
        }

        private async Task<string> FetchExpectedHashAsync(ProductUpdateRelease release)
        {
            using var response = await transport.SendAsync(release.ChecksumUrl, HttpCompletionOption.ResponseContentRead);
            if (!response.IsSuccessStatusCode)
            {
                throw new ProductUpdateException("checksum_download_failed");
            }

            var content = await response.Content.ReadAsStringAsync();
            var match = Sha256Pattern.Match(content);
            if (!match.Success)
            {
                throw new ProductUpdateException("checksum_invalid");
            }

            return match.Value;
        }

        private async Task<string> FetchDetachedSignatureAsync(ProductUpdateRelease release)
        {
            if (release.SignatureUrl == null)
            {
                return null;
            }

            using var response = await transport.SendAsync(release.SignatureUrl, HttpCompletionOption.ResponseContentRead);
            if (!response.IsSuccessStatusCode)
            {
                throw new ProductUpdateException("signature_download_failed");
            }

            var content = (await response.Content.ReadAsStringAsync() ?? "").Trim();
            if (content.Length == 0 || content.Length > 4096 || !Base64SignaturePattern.IsMatch(content))
            {
                throw new ProductUpdateException("signature_invalid");
            }

            return content;
        }

        private async Task DownloadInstallerAsync(ProductUpdateRelease release, string destinationPath)
        {
            using var response = await transport.SendAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                throw new ProductUpdateException("installer_download_failed");
            }

            var contentLength = response.Content.Headers.ContentLength.GetValueOrDefault();
            var downloaded = 0L;
            var buffer = new byte[81920];
            using var source = await response.Content.ReadAsStreamAsync();
            using var destination = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                buffer.Length,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            while (true)
            {
                var read = await source.ReadAsync(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer, 0, read);
                downloaded += read;
                if (contentLength > 0)
                {
                    var percent = (int)Math.Min(99, downloaded * 100 / contentLength);
                    Publish("downloading", "downloading", release, downloadPercent: percent);
                }
            }

            await destination.FlushAsync();
        }

        private static string CalculateSha256(Stream stream)
        {
            using var hash = SHA256.Create();
            stream.Position = 0;
            var digest = Convert.ToHexString(hash.ComputeHash(stream));
            stream.Position = 0;
            return digest;
        }

        private static Dictionary<string, string> ReleaseDetails(ProductUpdateRelease release)
        {
            return new Dictionary<string, string>
            {
                ["currentVersion"] = ProductVersion.Current,
                ["latestVersion"] = release?.Version ?? "",
                ["releaseUrl"] = release?.ReleaseUrl ?? ""
            };
        }

        private bool TryBeginOperation()
        {
            lock (gate)
            {
                if (operationActive)
                {
                    return false;
                }

                operationActive = true;
                return true;
            }
        }

        private void EndOperation()
        {
            lock (gate)
            {
                operationActive = false;
            }
        }

        private ProductUpdateStatus Publish(
            string state,
            string code,
            ProductUpdateRelease release = null,
            int downloadPercent = 0,
            bool canStart = false)
        {
            lock (gate)
            {
                if (release != null)
                {
                    latestRelease = release;
                }

                var selectedRelease = release ?? latestRelease;
                status = new ProductUpdateStatus
                {
                    State = state,
                    Code = code,
                    CurrentVersion = ProductVersion.Current,
                    LatestVersion = selectedRelease?.Version ?? "",
                    ReleaseUrl = selectedRelease?.ReleaseUrl ?? "",
                    UpdateAvailable = selectedRelease?.UpdateAvailable == true,
                    CanStart = canStart,
                    DownloadPercent = Math.Max(0, Math.Min(100, downloadPercent))
                };
                return status.Copy();
            }
        }

        private static void DeleteFileQuietly(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private sealed class ProductUpdateException : Exception
        {
            public ProductUpdateException(string code)
                : base(code)
            {
                Code = code;
            }

            public string Code { get; }
        }
    }
}
