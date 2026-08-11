using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using VibeDeck.Host.Diagnostics;
using VibeDeck.Host.Updates;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class ProductUpdateServiceTests : IDisposable
    {
        private const string ReleaseApiUrl = "https://api.github.com/repos/mabyes1/VibeDeck/releases/latest";
        private const string ReleaseVersion = "99.99.99";
        private const string InstallerFileName = "VibeDeck-Setup-99.99.99.exe";
        private const string DownloadBase = "https://github.com/mabyes1/VibeDeck/releases/download/v99.99.99/";

        private static readonly byte[] InstallerBytes = Encoding.ASCII.GetBytes("not-a-real-installer-payload-just-test-bytes");

        private readonly string tempRoot = Path.Combine(
            Path.GetTempPath(), "VibeDeckUpdateTests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
            }
        }

        [Fact]
        public async Task RelaxedModeLaunchesUnsignedReleaseAfterChecksumPasses()
        {
            var harness = CreateHarness(includeSignatureAsset: false, options: new ProductUpdateOptions());

            await harness.Service.DownloadAndLaunchAsync("trace-upd-0001");

            var status = harness.Service.GetStatus();
            Assert.Equal("launching", status.State);
            Assert.Equal("installer_started", status.Code);
            var call = Assert.Single(harness.Launcher.Calls);
            Assert.Equal(InstallerBytes, File.ReadAllBytes(call.InstallerPath));
        }

        [Fact]
        public async Task ChecksumMismatchNeverLaunchesTheInstaller()
        {
            var harness = CreateHarness(
                includeSignatureAsset: false,
                options: new ProductUpdateOptions(),
                checksumContent: new string('0', 64) + "  " + InstallerFileName);

            await harness.Service.DownloadAndLaunchAsync("trace-upd-0002");

            var status = harness.Service.GetStatus();
            Assert.Equal("failed", status.State);
            Assert.Equal("checksum_mismatch", status.Code);
            Assert.Empty(harness.Launcher.Calls);
            Assert.False(File.Exists(Path.Combine(harness.StagingDirectory, InstallerFileName)));
        }

        [Fact]
        public async Task RequireModeAbortsWithDistinctCodeWhenSignatureAssetIsMissing()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var harness = CreateHarness(
                includeSignatureAsset: false,
                options: new ProductUpdateOptions
                {
                    RequireSignedUpdates = true,
                    ReleaseSigningPublicKeyPem = key.ExportSubjectPublicKeyInfoPem()
                });

            await harness.Service.DownloadAndLaunchAsync("trace-upd-0003");

            var status = harness.Service.GetStatus();
            Assert.Equal("failed", status.State);
            Assert.Equal("signature_missing", status.Code);
            Assert.Empty(harness.Launcher.Calls);
        }

        [Fact]
        public async Task RequireModeAbortsWhenSignatureDoesNotVerify()
        {
            using var trustedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var forgedSignature = Convert.ToBase64String(
                attackerKey.SignHash(SHA256.HashData(InstallerBytes)));

            var harness = CreateHarness(
                includeSignatureAsset: true,
                options: new ProductUpdateOptions
                {
                    RequireSignedUpdates = true,
                    ReleaseSigningPublicKeyPem = trustedKey.ExportSubjectPublicKeyInfoPem()
                },
                signatureContent: forgedSignature);

            await harness.Service.DownloadAndLaunchAsync("trace-upd-0004");

            var status = harness.Service.GetStatus();
            Assert.Equal("failed", status.State);
            Assert.Equal("signature_invalid", status.Code);
            Assert.Empty(harness.Launcher.Calls);
        }

        [Fact]
        public async Task RequireModeLaunchesWhenDetachedSignatureIsValid()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var signature = Convert.ToBase64String(key.SignHash(SHA256.HashData(InstallerBytes)));

            var harness = CreateHarness(
                includeSignatureAsset: true,
                options: new ProductUpdateOptions
                {
                    RequireSignedUpdates = true,
                    ReleaseSigningPublicKeyPem = key.ExportSubjectPublicKeyInfoPem()
                },
                signatureContent: signature);

            await harness.Service.DownloadAndLaunchAsync("trace-upd-0005");

            var status = harness.Service.GetStatus();
            Assert.Equal("launching", status.State);
            Assert.Equal("installer_started", status.Code);
            Assert.Single(harness.Launcher.Calls);
        }

        [Fact]
        public async Task RequireModeWithoutPinnedKeyFailsClosed()
        {
            var harness = CreateHarness(
                includeSignatureAsset: false,
                options: new ProductUpdateOptions
                {
                    RequireSignedUpdates = true,
                    ReleaseSigningPublicKeyPem = ""
                });

            await harness.Service.DownloadAndLaunchAsync("trace-upd-0006");

            var status = harness.Service.GetStatus();
            Assert.Equal("failed", status.State);
            Assert.Equal("signature_key_missing", status.Code);
            Assert.Empty(harness.Launcher.Calls);
        }

        [Fact]
        public async Task RelaxedModeStillRejectsATamperedPublishedSignature()
        {
            using var trustedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var forgedSignature = Convert.ToBase64String(
                attackerKey.SignHash(SHA256.HashData(InstallerBytes)));

            var harness = CreateHarness(
                includeSignatureAsset: true,
                options: new ProductUpdateOptions
                {
                    RequireSignedUpdates = false,
                    ReleaseSigningPublicKeyPem = trustedKey.ExportSubjectPublicKeyInfoPem()
                },
                signatureContent: forgedSignature);

            await harness.Service.DownloadAndLaunchAsync("trace-upd-0007");

            var status = harness.Service.GetStatus();
            Assert.Equal("failed", status.State);
            Assert.Equal("signature_invalid", status.Code);
            Assert.Empty(harness.Launcher.Calls);
        }

        [Fact]
        public async Task BrokenAuthenticodeSignatureAbortsEvenInRelaxedMode()
        {
            var harness = CreateHarness(
                includeSignatureAsset: false,
                options: new ProductUpdateOptions(),
                authenticode: new AuthenticodeCheckResult(AuthenticodeStatus.Invalid, "", ""));

            await harness.Service.DownloadAndLaunchAsync("trace-upd-0008");

            var status = harness.Service.GetStatus();
            Assert.Equal("failed", status.State);
            Assert.Equal("authenticode_invalid", status.Code);
            Assert.Empty(harness.Launcher.Calls);
        }

        [Fact]
        public async Task AuthenticodeSignerOutsidePinnedThumbprintsAborts()
        {
            var harness = CreateHarness(
                includeSignatureAsset: false,
                options: new ProductUpdateOptions
                {
                    PinnedAuthenticodeThumbprints = new[] { "AABBCCDDEEFF00112233445566778899AABBCCDD" }
                },
                authenticode: new AuthenticodeCheckResult(
                    AuthenticodeStatus.Valid,
                    "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF",
                    "CN=Somebody Else"));

            await harness.Service.DownloadAndLaunchAsync("trace-upd-0009");

            var status = harness.Service.GetStatus();
            Assert.Equal("failed", status.State);
            Assert.Equal("authenticode_untrusted_publisher", status.Code);
            Assert.Empty(harness.Launcher.Calls);
        }

        [Fact]
        public async Task PinnedAuthenticodeSignerLaunches()
        {
            const string pinned = "AABBCCDDEEFF00112233445566778899AABBCCDD";
            var harness = CreateHarness(
                includeSignatureAsset: false,
                options: new ProductUpdateOptions
                {
                    PinnedAuthenticodeThumbprints = new[] { pinned }
                },
                authenticode: new AuthenticodeCheckResult(AuthenticodeStatus.Valid, pinned, "CN=VibeDeck"));

            await harness.Service.DownloadAndLaunchAsync("trace-upd-0010");

            var status = harness.Service.GetStatus();
            Assert.Equal("launching", status.State);
            Assert.Single(harness.Launcher.Calls);
        }

        [Fact]
        public void DetachedSignatureVerificationRoundTrips()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var hashHex = Convert.ToHexString(SHA256.HashData(InstallerBytes));
            var signature = Convert.ToBase64String(key.SignHash(Convert.FromHexString(hashHex)));
            var publicPem = key.ExportSubjectPublicKeyInfoPem();

            Assert.True(InstallerVerifier.VerifyDetachedSignature(hashHex, signature, publicPem));
            Assert.False(InstallerVerifier.VerifyDetachedSignature(
                Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })), signature, publicPem));
            Assert.False(InstallerVerifier.VerifyDetachedSignature(hashHex, "not-base64!!", publicPem));
            Assert.False(InstallerVerifier.VerifyDetachedSignature(hashHex, signature, ""));
        }

        private Harness CreateHarness(
            bool includeSignatureAsset,
            ProductUpdateOptions options,
            string checksumContent = null,
            string signatureContent = null,
            AuthenticodeCheckResult authenticode = null)
        {
            var staging = Path.Combine(tempRoot, "staging");
            options.StagingDirectoryOverride = staging;

            var transport = new StubTransport();
            transport.Set(ReleaseApiUrl, () => Json(BuildReleaseJson(includeSignatureAsset)));
            transport.Set(DownloadBase + InstallerFileName, () => Bytes(InstallerBytes));
            transport.Set(
                DownloadBase + InstallerFileName + ".sha256",
                () => Text(checksumContent ?? Convert.ToHexString(SHA256.HashData(InstallerBytes)) + "  " + InstallerFileName));
            if (includeSignatureAsset)
            {
                transport.Set(
                    DownloadBase + InstallerFileName + ".sig",
                    () => Text(signatureContent ?? ""));
            }

            var launcher = new StubLauncher();
            var verifier = new StubAuthenticodeVerifier(
                authenticode ?? new AuthenticodeCheckResult(AuthenticodeStatus.NotSigned, "", ""));
            var audit = new AuditTrailService(Path.Combine(tempRoot, "audit"));
            var service = new ProductUpdateService(audit, options, transport, launcher, verifier);
            return new Harness(service, launcher, staging);
        }

        private static string BuildReleaseJson(bool includeSignatureAsset)
        {
            var assets = new List<string>
            {
                Asset(InstallerFileName),
                Asset(InstallerFileName + ".sha256")
            };
            if (includeSignatureAsset)
            {
                assets.Add(Asset(InstallerFileName + ".sig"));
            }

            return "{" +
                $"\"tag_name\": \"v{ReleaseVersion}\"," +
                $"\"html_url\": \"https://github.com/mabyes1/VibeDeck/releases/tag/v{ReleaseVersion}\"," +
                $"\"assets\": [{string.Join(",", assets)}]" +
                "}";
        }

        private static string Asset(string name)
        {
            return $"{{ \"name\": \"{name}\", \"browser_download_url\": \"{DownloadBase}{name}\" }}";
        }

        private static HttpResponseMessage Json(string content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
        }

        private static HttpResponseMessage Text(string content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "text/plain")
            };
        }

        private static HttpResponseMessage Bytes(byte[] content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            };
        }

        private sealed class Harness
        {
            public Harness(ProductUpdateService service, StubLauncher launcher, string stagingDirectory)
            {
                Service = service;
                Launcher = launcher;
                StagingDirectory = stagingDirectory;
            }

            public ProductUpdateService Service { get; }
            public StubLauncher Launcher { get; }
            public string StagingDirectory { get; }
        }

        private sealed class StubTransport : IUpdateHttpTransport
        {
            private readonly Dictionary<string, Func<HttpResponseMessage>> responses =
                new Dictionary<string, Func<HttpResponseMessage>>(StringComparer.OrdinalIgnoreCase);

            public void Set(string url, Func<HttpResponseMessage> factory)
            {
                responses[url] = factory;
            }

            public Task<HttpResponseMessage> SendAsync(Uri uri, HttpCompletionOption completionOption)
            {
                if (responses.TryGetValue(uri.AbsoluteUri, out var factory))
                {
                    return Task.FromResult(factory());
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        private sealed class StubLauncher : IInstallerLauncher
        {
            public sealed record LaunchCall(string InstallerPath, string Arguments, string WorkingDirectory);

            public List<LaunchCall> Calls { get; } = new List<LaunchCall>();

            public bool Launch(string installerPath, string arguments, string workingDirectory)
            {
                // Prove the verified file is still readable (and unchanged) at
                // launch time while the service holds its deny-write handle.
                using (new FileStream(installerPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                }

                Calls.Add(new LaunchCall(installerPath, arguments, workingDirectory));
                return true;
            }
        }

        private sealed class StubAuthenticodeVerifier : IAuthenticodeVerifier
        {
            private readonly AuthenticodeCheckResult result;

            public StubAuthenticodeVerifier(AuthenticodeCheckResult result)
            {
                this.result = result;
            }

            public AuthenticodeCheckResult Check(string filePath)
            {
                return result;
            }
        }
    }
}
