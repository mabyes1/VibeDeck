using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using VibeDeck.Host.Security;
using Xunit;

namespace VibeDeck.Host.Tests
{
    // Hardening guarantees of the local HTTPS certificate chain: new roots are
    // name-constrained and EKU-limited, PFX files are no longer exported with an
    // empty password, and a legacy empty-password install is re-protected in place
    // without rolling the root (no phone-side re-trust).
    public sealed class LocalHttpsCertificateHardeningTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "VibeDeck-tests", Guid.NewGuid().ToString("N"));

        public LocalHttpsCertificateHardeningTests()
        {
            Directory.CreateDirectory(root);
            LocalHttpsCertificate.CertificateDirectoryOverride = root;
        }

        [Fact]
        public void New_root_is_name_constrained_and_pfx_needs_a_password()
        {
            var status = LocalHttpsCertificate.EnsureCurrent();
            Assert.True(status.Success, status.Error);
            Assert.True(status.RootCreated);

            using var rootCertificate = new X509Certificate2(LocalHttpsCertificate.RootCertificatePath);

            var nameConstraints = rootCertificate.Extensions["2.5.29.30"];
            Assert.NotNull(nameConstraints);
            Assert.True(nameConstraints.Critical);

            var eku = rootCertificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
            Assert.NotNull(eku);
            Assert.Contains(eku.EnhancedKeyUsages.Cast<Oid>(), oid => oid.Value == "1.3.6.1.5.5.7.3.1");

            // The pre-fix attack: any local reader loading the CA key with an empty
            // password. Both PFX files must now refuse the empty password.
            Assert.Throws<CryptographicException>(() => new X509Certificate2(
                LocalHttpsCertificate.RootPfxPath, string.Empty, X509KeyStorageFlags.EphemeralKeySet));
            Assert.Throws<CryptographicException>(() => new X509Certificate2(
                LocalHttpsCertificate.HostPfxPath, string.Empty, X509KeyStorageFlags.EphemeralKeySet));

            Assert.True(File.Exists(LocalHttpsCertificate.PfxPasswordPath));

            // The Host itself must still be able to serve TLS with the protected PFX.
            Assert.True(LocalHttpsCertificate.TryLoadServerCertificate(out var server, out var error), error);
            using (server)
            {
                Assert.True(server.HasPrivateKey);
            }
        }

        [Fact]
        public void Legacy_empty_password_root_is_kept_and_reprotected()
        {
            // Recreate the on-disk state of a pre-hardening install: a CA root whose
            // PFX was exported with an empty password (and no NameConstraints).
            string legacyThumbprint;
            using (var key = RSA.Create(2048))
            {
                var request = new CertificateRequest(
                    "CN=VibeDeck Local Root CA",
                    key,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
                request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
                using var certificate = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddDays(-1),
                    DateTimeOffset.UtcNow.AddYears(9));
                File.WriteAllBytes(LocalHttpsCertificate.RootCertificatePath, certificate.Export(X509ContentType.Cert));
                File.WriteAllBytes(LocalHttpsCertificate.RootPfxPath, certificate.Export(X509ContentType.Pfx, string.Empty));
                legacyThumbprint = certificate.Thumbprint;
            }

            var status = LocalHttpsCertificate.EnsureCurrent();
            Assert.True(status.Success, status.Error);

            // Upgrade must keep the exact same root so already-paired phones do not
            // have to re-trust anything.
            Assert.False(status.RootCreated);
            using (var rootCertificate = new X509Certificate2(LocalHttpsCertificate.RootCertificatePath))
            {
                Assert.Equal(legacyThumbprint, rootCertificate.Thumbprint);
            }

            // ...but the PFX on disk is no longer openable with the empty password.
            Assert.Throws<CryptographicException>(() => new X509Certificate2(
                LocalHttpsCertificate.RootPfxPath, string.Empty, X509KeyStorageFlags.EphemeralKeySet));
            Assert.True(File.Exists(LocalHttpsCertificate.PfxPasswordPath));

            // And HTTPS still works: a host certificate signed by the legacy root loads.
            Assert.True(LocalHttpsCertificate.TryLoadServerCertificate(out var server, out var error), error);
            using (server)
            {
                Assert.True(server.HasPrivateKey);
                Assert.Equal("CN=VibeDeck Local Root CA", server.Issuer);
            }
        }

        public void Dispose()
        {
            LocalHttpsCertificate.CertificateDirectoryOverride = null;
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch
            {
            }
        }
    }
}
