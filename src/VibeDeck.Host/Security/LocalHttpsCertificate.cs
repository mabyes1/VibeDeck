using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace VibeDeck.Host.Security
{
    public static class LocalHttpsCertificate
    {
        public const int HttpsPort = 5443;
        public const string RootPfxFileName = "vibedeck-root.pfx";
        public const string HostPfxFileName = "vibedeck-host.pfx";
        public const string RootCerFileName = "vibedeck-root.cer";
        public const string HostCerFileName = "vibedeck-host.cer";
        public const string CertificateStateFileName = "vibedeck-certificate-state.json";
        public const string PfxPasswordFileName = "vibedeck-pfx-password.bin";

        private const string NameConstraintsOid = "2.5.29.30";
        private const string ServerAuthEkuOid = "1.3.6.1.5.5.7.3.1";

        private static readonly TimeSpan RenewalWindow = TimeSpan.FromDays(30);
        private static readonly byte[] PfxPasswordEntropy = Encoding.UTF8.GetBytes("VibeDeck.Certs.PfxPassword.v1");

        /// <summary>Test hook (InternalsVisibleTo) so certificate tests never touch real product state.</summary>
        internal static string CertificateDirectoryOverride;

        public static string CertificateDirectory => CertificateDirectoryOverride ?? AppPaths.CertsDirectory;

        public static string RootPfxPath => Path.Combine(CertificateDirectory, RootPfxFileName);
        public static string HostPfxPath => Path.Combine(CertificateDirectory, HostPfxFileName);
        public static string RootCertificatePath => Path.Combine(CertificateDirectory, RootCerFileName);
        public static string HostCertificatePath => Path.Combine(CertificateDirectory, HostCerFileName);
        public static string CertificateStatePath => Path.Combine(CertificateDirectory, CertificateStateFileName);
        public static string PfxPasswordPath => Path.Combine(CertificateDirectory, PfxPasswordFileName);

        public static bool IsConfigured =>
            File.Exists(HostPfxPath) &&
            File.Exists(RootCertificatePath) &&
            File.Exists(HostCertificatePath);

        public static LocalHttpsCertificateStatus EnsureCurrent()
        {
            var dnsNames = GetDnsNames().ToList();
            var ipAddresses = GetCertificateIpAddresses().ToList();

            try
            {
                Directory.CreateDirectory(CertificateDirectory);

                // Existing installs exported PFX files with an empty password. Re-protect
                // them in place (same certificates, same keys) so HTTPS keeps working and
                // phones never need to re-trust the root because of this upgrade.
                UpgradeLegacyPfxProtection();

                var rootCreated = false;
                var hostCreated = false;
                var hostReissued = false;
                var rootCertificate = LoadRootCertificate();
                if (rootCertificate == null ||
                    ShouldRenew(rootCertificate, TimeSpan.FromDays(90)) ||
                    RootConstraintsStale(rootCertificate))
                {
                    rootCertificate?.Dispose();
                    rootCertificate = CreateRootCertificate();
                    rootCreated = true;
                }

                try
                {
                    if (!IsHostCertificateCurrent(rootCertificate, dnsNames, ipAddresses))
                    {
                        CreateHostCertificate(rootCertificate, dnsNames, ipAddresses);
                        hostCreated = true;
                        hostReissued = !rootCreated;
                    }
                }
                finally
                {
                    rootCertificate.Dispose();
                }

                return new LocalHttpsCertificateStatus
                {
                    Success = true,
                    RootCreated = rootCreated,
                    HostCreated = hostCreated,
                    HostReissued = hostReissued,
                    DnsNames = dnsNames,
                    IpAddresses = ipAddresses
                };
            }
            catch (Exception ex)
            {
                return new LocalHttpsCertificateStatus
                {
                    Success = false,
                    Error = ex.Message,
                    DnsNames = dnsNames,
                    IpAddresses = ipAddresses
                };
            }
        }

        public static bool TryLoadServerCertificate(out X509Certificate2 certificate, out string error)
        {
            certificate = null;
            error = null;

            try
            {
                if (!File.Exists(HostPfxPath))
                {
                    error = $"HTTPS certificate not found at {HostPfxPath}.";
                    return false;
                }

                certificate = LoadPfx(HostPfxPath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static X509Certificate2 LoadRootCertificate()
        {
            if (!File.Exists(RootPfxPath))
            {
                return null;
            }

            var certificate = LoadPfx(RootPfxPath);
            if (certificate.HasPrivateKey)
            {
                return certificate;
            }

            certificate.Dispose();
            return null;
        }

        private static X509Certificate2 LoadPfx(string path)
        {
            // No Exportable: runtime consumers (Kestrel TLS, leaf signing) only need to
            // use the key, never to re-export it. Export happens solely inside the
            // legacy-upgrade path below with an explicitly widened flag set.
            var storedPassword = TryGetStoredPfxPassword();
            if (storedPassword != null)
            {
                try
                {
                    return new X509Certificate2(path, storedPassword, X509KeyStorageFlags.UserKeySet);
                }
                catch (CryptographicException)
                {
                    // Fall through: the file may still be a legacy empty-password PFX.
                }
            }

            return new X509Certificate2(path, string.Empty, X509KeyStorageFlags.UserKeySet);
        }

        private static string TryGetStoredPfxPassword()
        {
            return ProtectedSecretFile.TryRead(PfxPasswordPath, PfxPasswordEntropy, out var secret)
                ? Convert.ToBase64String(secret)
                : null;
        }

        private static string GetOrCreatePfxPassword()
        {
            var existing = TryGetStoredPfxPassword();
            if (existing != null)
            {
                return existing;
            }

            if (File.Exists(PfxPasswordPath))
            {
                // The password file belongs to a different Windows account (DPAPI
                // current-user). Never clobber it; callers degrade to the legacy
                // empty-password behavior instead of destroying the owner's state.
                return string.Empty;
            }

            var secret = RandomNumberGenerator.GetBytes(32);
            return ProtectedSecretFile.TryWrite(PfxPasswordPath, PfxPasswordEntropy, secret)
                ? Convert.ToBase64String(secret)
                : string.Empty;
        }

        /// <summary>
        /// Re-exports legacy empty-password PFX files with a DPAPI-protected random
        /// password. Certificates and private keys are unchanged, so the phone-side
        /// trust of the root and the current HTTPS endpoint identity both survive.
        /// Any failure leaves the legacy file in place and HTTPS keeps working.
        /// </summary>
        private static void UpgradeLegacyPfxProtection()
        {
            foreach (var path in new[] { RootPfxPath, HostPfxPath })
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    X509Certificate2 legacy;
                    try
                    {
                        legacy = new X509Certificate2(
                            path,
                            string.Empty,
                            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
                    }
                    catch (CryptographicException)
                    {
                        // Already password protected (or unreadable): nothing to upgrade.
                        continue;
                    }

                    using (legacy)
                    {
                        if (!legacy.HasPrivateKey)
                        {
                            continue;
                        }

                        var password = GetOrCreatePfxPassword();
                        if (string.IsNullOrEmpty(password))
                        {
                            // DPAPI storage unavailable; keep the legacy file untouched.
                            continue;
                        }

                        var protectedPfx = legacy.Export(X509ContentType.Pfx, password);
                        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            File.WriteAllBytes(temporaryPath, protectedPfx);
                            File.Move(temporaryPath, path, overwrite: true);
                        }
                        finally
                        {
                            if (File.Exists(temporaryPath))
                            {
                                File.Delete(temporaryPath);
                            }
                        }
                    }
                }
                catch (Exception ex) when (
                    ex is CryptographicException ||
                    ex is IOException ||
                    ex is UnauthorizedAccessException)
                {
                    // Best effort: the legacy PFX still loads via the empty-password
                    // fallback, so startup and HTTPS must not fail here.
                }
            }
        }

        private static X509Certificate2 CreateRootCertificate()
        {
            using (var rootKey = RSA.Create(4096))
            {
                var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
                var notAfter = notBefore.AddYears(10);
                var request = new CertificateRequest(
                    "CN=VibeDeck Local Root CA",
                    rootKey,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                request.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(true, false, 0, true));
                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(
                        X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                        true));

                // Pin what this CA may ever vouch for: local host names and private LAN
                // ranges only. A stolen root key can then no longer mint certificates
                // for arbitrary public hosts that chain-validating clients accept.
                request.CertificateExtensions.Add(BuildNameConstraintsExtension());

                // EKU on the CA: Windows intersects EKU along the chain, so leaves
                // signed by this root are only ever usable for TLS server auth.
                request.CertificateExtensions.Add(
                    new X509EnhancedKeyUsageExtension(
                        new OidCollection { new Oid(ServerAuthEkuOid) },
                        false));
                request.CertificateExtensions.Add(
                    new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

                using (var certificate = request.CreateSelfSigned(notBefore, notAfter))
                {
                    File.WriteAllBytes(RootCertificatePath, certificate.Export(X509ContentType.Cert));
                    File.WriteAllBytes(
                        RootPfxPath,
                        certificate.Export(X509ContentType.Pfx, GetOrCreatePfxPassword()));
                }
            }

            return LoadPfx(RootPfxPath);
        }

        /// <summary>
        /// A root that carries NameConstraints minted for a previous machine name can no
        /// longer vouch for the current host names, so it must be rolled. Legacy roots
        /// without the extension are deliberately kept: recreating them would force every
        /// paired phone to re-trust the root, which is worse than the missing pin.
        /// </summary>
        private static bool RootConstraintsStale(X509Certificate2 rootCertificate)
        {
            try
            {
                var existing = rootCertificate.Extensions[NameConstraintsOid];
                if (existing == null)
                {
                    return false;
                }

                var expected = BuildNameConstraintsExtension();
                return !existing.RawData.AsSpan().SequenceEqual(expected.RawData);
            }
            catch
            {
                return false;
            }
        }

        private static X509Extension BuildNameConstraintsExtension()
        {
            // RFC 5280 NameConstraints, permittedSubtrees only:
            //   dNSName: localhost, <hostname>, local (covers *.local / mDNS)
            //   iPAddress: loopback + RFC1918 + CGNAT ranges (IPv4 base+mask form)
            var writer = new AsnWriter(AsnEncodingRules.DER);
            using (writer.PushSequence())
            {
                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
                {
                    foreach (var dnsName in GetConstraintDnsNames())
                    {
                        using (writer.PushSequence())
                        {
                            writer.WriteCharacterString(
                                UniversalTagNumber.IA5String,
                                dnsName,
                                new Asn1Tag(TagClass.ContextSpecific, 2));
                        }
                    }

                    foreach (var (network, mask) in GetConstraintIpRanges())
                    {
                        using (writer.PushSequence())
                        {
                            var value = new byte[8];
                            network.CopyTo(value, 0);
                            mask.CopyTo(value, 4);
                            writer.WriteOctetString(value, new Asn1Tag(TagClass.ContextSpecific, 7));
                        }
                    }
                }
            }

            return new X509Extension(new Oid(NameConstraintsOid), writer.Encode(), critical: true);
        }

        private static IEnumerable<string> GetConstraintDnsNames()
        {
            var hostName = Dns.GetHostName();
            return new[] { "localhost", hostName, "local" }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal);
        }

        private static IEnumerable<(byte[] Network, byte[] Mask)> GetConstraintIpRanges()
        {
            yield return (new byte[] { 127, 0, 0, 0 }, new byte[] { 255, 0, 0, 0 });
            yield return (new byte[] { 10, 0, 0, 0 }, new byte[] { 255, 0, 0, 0 });
            yield return (new byte[] { 172, 16, 0, 0 }, new byte[] { 255, 240, 0, 0 });
            yield return (new byte[] { 192, 168, 0, 0 }, new byte[] { 255, 255, 0, 0 });
            yield return (new byte[] { 100, 64, 0, 0 }, new byte[] { 255, 192, 0, 0 });
        }

        private static bool IsHostCertificateCurrent(
            X509Certificate2 rootCertificate,
            IReadOnlyList<string> dnsNames,
            IReadOnlyList<string> ipAddresses)
        {
            if (!File.Exists(HostPfxPath) ||
                !File.Exists(HostCertificatePath) ||
                !File.Exists(RootCertificatePath) ||
                !File.Exists(CertificateStatePath))
            {
                return false;
            }

            CertificateState state;
            try
            {
                state = JsonSerializer.Deserialize<CertificateState>(File.ReadAllText(CertificateStatePath));
            }
            catch
            {
                return false;
            }

            if (state == null ||
                !StringSetsEqual(state.DnsNames, dnsNames) ||
                !StringSetsEqual(state.IpAddresses, ipAddresses) ||
                !string.Equals(state.RootThumbprint, rootCertificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                using (var hostCertificate = LoadPfx(HostPfxPath))
                {
                    return hostCertificate.HasPrivateKey &&
                        !ShouldRenew(hostCertificate, RenewalWindow) &&
                        string.Equals(state.HostThumbprint, hostCertificate.Thumbprint, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        private static void CreateHostCertificate(
            X509Certificate2 rootCertificate,
            IReadOnlyList<string> dnsNames,
            IReadOnlyList<string> ipAddresses)
        {
            using (var hostKey = RSA.Create(2048))
            {
                var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
                var notAfter = notBefore.AddYears(2);
                var request = new CertificateRequest(
                    "CN=VibeDeck Local Host",
                    hostKey,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                request.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                        true));

                var oids = new OidCollection();
                oids.Add(new Oid("1.3.6.1.5.5.7.3.1"));
                request.CertificateExtensions.Add(
                    new X509EnhancedKeyUsageExtension(oids, false));

                var san = new SubjectAlternativeNameBuilder();
                foreach (var dnsName in dnsNames)
                {
                    san.AddDnsName(dnsName);
                }

                foreach (var ipAddress in ipAddresses)
                {
                    san.AddIpAddress(IPAddress.Parse(ipAddress));
                }

                request.CertificateExtensions.Add(san.Build(false));
                request.CertificateExtensions.Add(
                    new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

                using (var hostWithoutKey = request.Create(rootCertificate, notBefore, notAfter, NewRandomSerial()))
                using (var hostCertificate = hostWithoutKey.CopyWithPrivateKey(hostKey))
                {
                    File.WriteAllBytes(HostCertificatePath, hostCertificate.Export(X509ContentType.Cert));
                    File.WriteAllBytes(
                        HostPfxPath,
                        hostCertificate.Export(X509ContentType.Pfx, GetOrCreatePfxPassword()));

                    var state = new CertificateState
                    {
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                        DnsNames = dnsNames.ToList(),
                        IpAddresses = ipAddresses.ToList(),
                        RootThumbprint = rootCertificate.Thumbprint,
                        HostThumbprint = hostCertificate.Thumbprint,
                        HostNotAfterUtc = hostCertificate.NotAfter.ToUniversalTime()
                    };
                    File.WriteAllText(
                        CertificateStatePath,
                        JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
        }

        private static byte[] NewRandomSerial()
        {
            var serial = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(serial);
            }

            return serial;
        }

        private static bool ShouldRenew(X509Certificate2 certificate, TimeSpan window)
        {
            return DateTimeOffset.UtcNow.Add(window) >= new DateTimeOffset(certificate.NotAfter);
        }

        private static bool StringSetsEqual(IEnumerable<string> left, IEnumerable<string> right)
        {
            var leftSet = new HashSet<string>(left ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var rightSet = new HashSet<string>(right ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return leftSet.SetEquals(rightSet);
        }

        private static IEnumerable<string> GetDnsNames()
        {
            var hostName = Dns.GetHostName();
            return new[] { "localhost", hostName, $"{hostName}.local" }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> GetCertificateIpAddresses()
        {
            return new[] { "127.0.0.1" }
                .Concat(GetLanIpAddresses())
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> GetLanIpAddresses()
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var properties = networkInterface.GetIPProperties();
                foreach (var address in properties.UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    var text = address.Address.ToString();
                    if (text.StartsWith("169.254.", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    yield return text;
                }
            }
        }

        private sealed class CertificateState
        {
            public DateTimeOffset CreatedAtUtc { get; set; }
            public List<string> DnsNames { get; set; }
            public List<string> IpAddresses { get; set; }
            public string RootThumbprint { get; set; }
            public string HostThumbprint { get; set; }
            public DateTime HostNotAfterUtc { get; set; }
        }
    }

    public sealed class LocalHttpsCertificateStatus
    {
        public bool Success { get; set; }
        public bool RootCreated { get; set; }
        public bool HostCreated { get; set; }
        public bool HostReissued { get; set; }
        public string Error { get; set; }
        public IReadOnlyList<string> DnsNames { get; set; }
        public IReadOnlyList<string> IpAddresses { get; set; }
    }
}
