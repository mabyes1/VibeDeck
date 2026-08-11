using System;
using System.IO;
using System.Security.Cryptography;

namespace VibeDeck.Host.Security
{
    /// <summary>
    /// Small binary secret files protected with Windows DPAPI (current-user scope).
    /// Used for the certificate PFX password and the trusted-device store MAC key so
    /// that a different local account which can read the bytes on disk still cannot
    /// use them. Complements (and does not replace) the Setup-applied directory ACLs.
    /// </summary>
    internal static class ProtectedSecretFile
    {
        /// <summary>
        /// Reads and unprotects a DPAPI secret file. Returns false when the file is
        /// missing, unreadable, or was protected by a different Windows account.
        /// </summary>
        internal static bool TryRead(string path, byte[] entropy, out byte[] secret)
        {
            secret = null;
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                var protectedBytes = File.ReadAllBytes(path);
                if (protectedBytes.Length == 0)
                {
                    return false;
                }

                var unprotected = ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.CurrentUser);
                if (unprotected == null || unprotected.Length == 0)
                {
                    return false;
                }

                secret = unprotected;
                return true;
            }
            catch (Exception ex) when (
                ex is CryptographicException ||
                ex is IOException ||
                ex is UnauthorizedAccessException)
            {
                secret = null;
                return false;
            }
        }

        /// <summary>
        /// Protects and writes a secret atomically. Returns false instead of throwing so
        /// that a broken ACL never turns startup into a crash; callers degrade gracefully.
        /// </summary>
        internal static bool TryWrite(string path, byte[] entropy, byte[] secret)
        {
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var protectedBytes = ProtectedData.Protect(secret, entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(temporaryPath, protectedBytes);
                File.Move(temporaryPath, path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (
                ex is CryptographicException ||
                ex is IOException ||
                ex is UnauthorizedAccessException)
            {
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch
                {
                    // A stray .tmp file must never break the caller.
                }
            }
        }
    }
}
