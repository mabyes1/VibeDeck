using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace VibeDeck.Host.Updates
{
    public enum AuthenticodeStatus
    {
        /// <summary>File carries no Authenticode signature at all.</summary>
        NotSigned,

        /// <summary>Signature present and WinVerifyTrust reports it trusted.</summary>
        Valid,

        /// <summary>Signature present but broken, untrusted, or verification failed.</summary>
        Invalid
    }

    public sealed class AuthenticodeCheckResult
    {
        public AuthenticodeCheckResult(AuthenticodeStatus status, string signerThumbprint, string signerSubject)
        {
            Status = status;
            SignerThumbprint = signerThumbprint ?? "";
            SignerSubject = signerSubject ?? "";
        }

        public AuthenticodeStatus Status { get; }
        public string SignerThumbprint { get; }
        public string SignerSubject { get; }
    }

    /// <summary>
    /// OS-dependent Authenticode verification seam (stubbed in tests).
    /// </summary>
    public interface IAuthenticodeVerifier
    {
        AuthenticodeCheckResult Check(string filePath);
    }

    public sealed class InstallerVerifier : IAuthenticodeVerifier
    {
        /// <summary>
        /// Verifies a detached ECDSA signature (IEEE P1363 format, as produced by
        /// scripts/sign-release.ps1) over the SHA-256 hash of the installer.
        /// Pure crypto - deterministic and safe to call in tests with a test key.
        /// </summary>
        /// <param name="sha256HashHex">Hash of the EXACT bytes that will be executed.</param>
        public static bool VerifyDetachedSignature(string sha256HashHex, string signatureBase64, string publicKeyPem)
        {
            if (string.IsNullOrWhiteSpace(sha256HashHex) ||
                string.IsNullOrWhiteSpace(signatureBase64) ||
                string.IsNullOrWhiteSpace(publicKeyPem))
            {
                return false;
            }

            try
            {
                var hash = Convert.FromHexString(sha256HashHex);
                var signature = Convert.FromBase64String(signatureBase64.Trim());
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(publicKeyPem);
                return ecdsa.VerifyHash(hash, signature);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Full Authenticode verification via WinVerifyTrust (chain building,
        /// hash check, trusted root). Same trust decision signtool/PowerShell
        /// Get-AuthenticodeSignature would make; consistent with the cloudflared
        /// publisher check in scripts/package-windows-setup.ps1.
        /// </summary>
        public AuthenticodeCheckResult Check(string filePath)
        {
            var status = VerifyWithWinVerifyTrust(filePath);
            if (status != AuthenticodeStatus.Valid)
            {
                return new AuthenticodeCheckResult(status, "", "");
            }

            try
            {
                using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
                return new AuthenticodeCheckResult(AuthenticodeStatus.Valid, signer.Thumbprint, signer.Subject);
            }
            catch
            {
                // Signed and trusted but the signer certificate could not be read:
                // treat as invalid rather than skipping the publisher pin.
                return new AuthenticodeCheckResult(AuthenticodeStatus.Invalid, "", "");
            }
        }

        private const uint TrustENoSignature = 0x800B0100;
        private const uint TrustESubjectFormUnknown = 0x800B0003;
        private const uint TrustEProviderUnknown = 0x800B0001;

        private static AuthenticodeStatus VerifyWithWinVerifyTrust(string filePath)
        {
            if (!OperatingSystem.IsWindows())
            {
                return AuthenticodeStatus.Invalid;
            }

            var actionId = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
            var fileInfo = new WinTrustFileInfo
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                FilePath = filePath,
                FileHandle = IntPtr.Zero,
                KnownSubject = IntPtr.Zero
            };
            var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            try
            {
                Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);
                var trustData = new WinTrustData
                {
                    StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                    PolicyCallbackData = IntPtr.Zero,
                    SipClientData = IntPtr.Zero,
                    UiChoice = 2,            // WTD_UI_NONE
                    RevocationChecks = 0,    // WTD_REVOKE_NONE (offline-tolerant)
                    UnionChoice = 1,         // WTD_CHOICE_FILE
                    FileInfo = fileInfoPtr,
                    StateAction = 1,         // WTD_STATEACTION_VERIFY
                    StateData = IntPtr.Zero,
                    UrlReference = IntPtr.Zero,
                    ProvFlags = 0x00000010 | 0x00000080, // WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL
                    UiContext = 0
                };
                var result = (uint)WinVerifyTrust(IntPtr.Zero, ref actionId, ref trustData);

                // Release provider state.
                trustData.StateAction = 2; // WTD_STATEACTION_CLOSE
                WinVerifyTrust(IntPtr.Zero, ref actionId, ref trustData);

                if (result == 0)
                {
                    return AuthenticodeStatus.Valid;
                }

                if (result == TrustENoSignature ||
                    result == TrustESubjectFormUnknown ||
                    result == TrustEProviderUnknown)
                {
                    return AuthenticodeStatus.NotSigned;
                }

                return AuthenticodeStatus.Invalid;
            }
            finally
            {
                Marshal.FreeHGlobal(fileInfoPtr);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint StructSize;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustData
        {
            public uint StructSize;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProvFlags;
            public uint UiContext;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid actionId, ref WinTrustData trustData);
    }
}
