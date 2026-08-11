using System;

namespace VibeDeck.Host.Updates
{
    /// <summary>
    /// Update-integrity policy. This is THE single place that decides how strictly
    /// downloaded installers are verified before they are launched (elevated!).
    ///
    /// Verification layers:
    ///  1. SHA-256 checksum (always) - detects corruption only, NOT tampering,
    ///     because the .sha256 comes from the same origin as the installer.
    ///  2. Detached ECDSA P-256 release signature (VibeDeck-Setup-x.y.z.exe.sig,
    ///     produced offline by scripts/sign-release.ps1) verified against the
    ///     public key pinned below. This defeats a compromised GitHub release:
    ///     an attacker who can publish assets still cannot produce a valid .sig
    ///     without the offline private key.
    ///  3. Authenticode (WinVerifyTrust): whenever the downloaded installer
    ///     carries an Authenticode signature it MUST verify as trusted, and if
    ///     publisher thumbprints are pinned the signer must match one of them.
    ///     An unsigned installer is tolerated (current releases are unsigned;
    ///     production code signing is a planned future step).
    /// </summary>
    public sealed class ProductUpdateOptions
    {
        /// <summary>
        /// Enforcement default for the detached release signature.
        ///
        /// false (current): releases without a .sig asset still install. This is
        ///   REQUIRED for backward compatibility today because every published
        ///   release is unsigned and <see cref="PinnedReleaseSigningPublicKeyPem"/>
        ///   is not yet provisioned. If a .sig asset IS present it is always
        ///   verified and a bad signature always aborts (fail closed on tamper).
        ///
        /// true: an update without a valid detached signature never launches
        ///   (distinct failure codes: signature_missing / signature_invalid /
        ///   signature_key_missing).
        ///
        /// Remaining steps to turn enforcement on (the key is already pinned):
        ///   1. DONE - keypair generated, public key pinned below.
        ///   2. Ship ONE release signed with that key: build the installer, then
        ///      run scripts/sign-release.ps1 -Sign -KeyPath &lt;offline-path&gt;
        ///      -InstallerPath &lt;exe&gt;, and upload all three assets (.exe,
        ///      .exe.sha256, .exe.sig).
        ///   3. In the NEXT release, flip this constant to true.
        /// Do not flip before a signed release exists: hosts running the
        /// enforcing build would have nothing valid to update to.
        /// </summary>
        public const bool RequireSignedUpdatesDefault = false;

        /// <summary>
        /// SubjectPublicKeyInfo PEM of the offline release-signing key
        /// (ECDSA P-256). The matching private key is held offline by the project
        /// owner and must never reach the repository, CI, or the update origin.
        ///
        /// With this pinned, a published .sig is ALWAYS verified and a tampered
        /// installer is refused even in relaxed mode. A release that ships no .sig
        /// at all is still accepted until RequireSignedUpdatesDefault is true.
        /// </summary>
        public const string PinnedReleaseSigningPublicKeyPem = @"-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEc8PP7vy6yytSScDnlUbdDo9vBz2K
mfK5BDAtMcs74b8qjj53DBINz1TwOVzl1JnAW9JayaGf3lof5/650DG3Qw==
-----END PUBLIC KEY-----";

        /// <summary>
        /// SHA-1 thumbprints of Authenticode signing certificates that are
        /// allowed to sign VibeDeck-Setup-*.exe. Empty = any signer whose
        /// signature chains to a trusted root is accepted (the signature itself
        /// must still be valid). Populate once production code signing exists.
        /// </summary>
        public static readonly string[] PinnedAuthenticodeThumbprintsDefault = Array.Empty<string>();

        public bool RequireSignedUpdates { get; set; } = RequireSignedUpdatesDefault;

        public string ReleaseSigningPublicKeyPem { get; set; } = PinnedReleaseSigningPublicKeyPem;

        public string[] PinnedAuthenticodeThumbprints { get; set; } = PinnedAuthenticodeThumbprintsDefault;

        /// <summary>Test seam: overrides %LocalAppData%\VibeDeck\updates staging directory.</summary>
        internal string StagingDirectoryOverride { get; set; }
    }
}
