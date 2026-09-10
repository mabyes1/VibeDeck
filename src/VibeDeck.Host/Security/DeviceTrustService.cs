using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VibeDeck.Host.Security
{
    public sealed class DeviceTrustService
    {
        public const string HeaderName = "X-VibeDeck-Device-Token";
        public const string CookieName = "VibeDeck-Device-Token";
        public const string ClientInstanceHeaderName = "X-VibeDeck-Client-Instance";
        public const string DeviceModelHeaderName = "X-VibeDeck-Device-Model";

        private static readonly TimeSpan LastSeenSaveDebounce = TimeSpan.FromSeconds(30);

        private readonly object sync = new object();
        private readonly TrustedDeviceStore deviceStore;
        private readonly PairingApprovalRegistry pairingApprovals = new PairingApprovalRegistry();
        private List<TrustedDeviceRecord> devices;
        private bool devicesDirty;
        private DateTimeOffset lastDevicesFlushAt = DateTimeOffset.MinValue;

        public DeviceTrustService()
            : this(AppPaths.DevicesDirectory)
        {
        }

        public DeviceTrustService(string devicesDirectory)
        {
            deviceStore = new TrustedDeviceStore(devicesDirectory);
            devices = deviceStore.Load(out var storeFormatOutdated);
            if (DeviceIdentityPolicy.NormalizeRecords(devices) || storeFormatOutdated)
            {
                devicesDirty = true;
                try
                {
                    SaveDevices(force: true);
                }
                catch (IOException)
                {
                    // Keep valid in-memory trust even when an old install has bad ACLs.
                }
                catch (UnauthorizedAccessException)
                {
                    // Setup repairs ProgramData permissions; retry after that repair.
                }
            }
        }

        public PairingApprovalRequestResult RequestApproval(
            string name,
            string platform,
            string model,
            string clientInstanceId,
            string userAgent,
            string remoteAddress)
        {
            lock (sync)
            {
                return pairingApprovals.Request(
                    name,
                    platform,
                    model,
                    clientInstanceId,
                    userAgent,
                    remoteAddress,
                    DateTimeOffset.UtcNow);
            }
        }

        public List<PairingApprovalSummary> GetPendingApprovals()
        {
            lock (sync)
            {
                return pairingApprovals.GetPending(DateTimeOffset.UtcNow);
            }
        }

        public DeviceTrustActionResult ApproveRequest(string requestId)
        {
            lock (sync)
            {
                if (!pairingApprovals.TryGetPending(requestId, DateTimeOffset.UtcNow, out var request))
                    return DeviceTrustActionResult.Fail("Pairing request expired or was not found.");

                var originalDevices = devices.Select(CloneDevice).ToList();
                var originalDirty = devicesDirty;
                var token = PairingApprovalRegistry.CreateOpaqueToken(40);
                var device = DeviceIdentityPolicy.FindPairingContinuation(devices, request);
                var continued = device != null;
                if (device == null)
                {
                    device = new TrustedDeviceRecord
                    {
                        DeviceId = PairingApprovalRegistry.CreateOpaqueToken(16),
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    devices.Add(device);
                }

                device.Name = request.Name;
                device.Model = request.Model;
                device.ClientInstanceId = request.ClientInstanceId;
                device.TokenHash = deviceStore.HashDeviceToken(token);
                device.LastSeenAt = DateTimeOffset.UtcNow;
                device.LastRemoteAddress = request.RemoteAddress;
                device.LastUserAgent = request.UserAgent;

                if (!string.IsNullOrWhiteSpace(request.ClientInstanceId))
                {
                    devices.RemoveAll(item =>
                        !ReferenceEquals(item, device) &&
                        string.Equals(item.ClientInstanceId, request.ClientInstanceId, StringComparison.Ordinal));
                }
                try
                {
                    SaveDevices(force: true);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    devices = originalDevices;
                    devicesDirty = originalDirty;
                    return DeviceTrustActionResult.Fail("Pairing could not be saved. Repair the VibeDeck ProgramData permissions and try again.");
                }

                pairingApprovals.MarkApproved(request, device.DeviceId, token, continued);
                return new DeviceTrustActionResult
                {
                    Success = true,
                    Message = continued ? $"{device.Name} pairing continued." : $"{device.Name} approved."
                };
            }
        }

        public DeviceTrustActionResult DenyRequest(string requestId)
        {
            lock (sync)
            {
                return pairingApprovals.Deny(requestId);
            }
        }

        public PairingApprovalPollResult PollApproval(string requestId, string requestSecret)
        {
            lock (sync)
            {
                return pairingApprovals.Poll(requestId, requestSecret, DateTimeOffset.UtcNow);
            }
        }

        public DeviceTrustStatus GetStatus(string deviceToken, string remoteAddress, string userAgent, bool isLocalRequest, bool hostAuthenticated)
        {
            return GetStatus(deviceToken, remoteAddress, userAgent, isLocalRequest, hostAuthenticated, "", "");
        }

        public DeviceTrustStatus GetStatus(
            string deviceToken,
            string remoteAddress,
            string userAgent,
            bool isLocalRequest,
            bool hostAuthenticated,
            string model,
            string clientInstanceId)
        {
            lock (sync)
            {
                var now = DateTimeOffset.UtcNow;
                var device = FindTrustedDevice(deviceToken);
                if (device != null)
                {
                    UpdateDeviceIdentity(device, model, clientInstanceId, userAgent);
                    TouchDevice(device, remoteAddress, userAgent);
                }

                return new DeviceTrustStatus
                {
                    Trusted = isLocalRequest || hostAuthenticated || device != null,
                    LocalRequest = isLocalRequest,
                    DeviceHeader = HeaderName,
                    PairedDeviceCount = isLocalRequest || hostAuthenticated ? devices.Count : device == null ? 0 : 1,
                    CurrentDevice = device == null ? null : DeviceSummary.From(device, now),
                    Devices = isLocalRequest || hostAuthenticated
                        ? devices.Select(item => DeviceSummary.From(item, now)).ToList()
                        : new List<DeviceSummary>()
                };
            }
        }

        public bool IsTrusted(string deviceToken, string remoteAddress, string userAgent)
        {
            lock (sync)
            {
                var device = FindTrustedDevice(deviceToken);
                if (device == null)
                {
                    return false;
                }

                TouchDevice(device, remoteAddress, userAgent);
                return true;
            }
        }

        /// <summary>
        /// Raised after a device loses trust. Subscribers end whatever that device is
        /// already doing; removing it from the store only stops it starting something new.
        /// Raised outside the lock so a subscriber can never deadlock the store.
        /// </summary>
        public event Action<string> DeviceRevoked;

        /// <summary>Raised after every pairing is removed.</summary>
        public event Action DevicesCleared;

        /// <summary>
        /// Device id currently backing this token, or null when the token is unknown.
        /// Unlike <see cref="IsTrusted"/> this records no activity, so it is safe to call
        /// on a hot path.
        /// </summary>
        public string ResolveDeviceId(string deviceToken)
        {
            lock (sync)
            {
                return FindTrustedDevice(deviceToken)?.DeviceId;
            }
        }

        /// <summary>Whether this device id is still paired. No side effects.</summary>
        public bool IsDeviceTrusted(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return false;
            }

            lock (sync)
            {
                return devices.Any(device => string.Equals(device.DeviceId, deviceId, StringComparison.Ordinal));
            }
        }

        public DeviceTrustActionResult RevokeDevice(string deviceId)
        {
            lock (sync)
            {
                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    return DeviceTrustActionResult.Fail("Device id is required.");
                }

                var removed = devices.RemoveAll(device => string.Equals(device.DeviceId, deviceId, StringComparison.Ordinal));
                if (removed <= 0)
                {
                    return DeviceTrustActionResult.Fail("Device was not found.");
                }

                SaveDevices(force: true);
            }

            DeviceRevoked?.Invoke(deviceId);
            return new DeviceTrustActionResult
            {
                Success = true,
                Message = "Device revoked."
            };
        }

        public DeviceTrustActionResult ClearDevices()
        {
            int removed;
            lock (sync)
            {
                removed = devices.Count;
                devices.Clear();
                pairingApprovals.Clear();
                SaveDevices(force: true);
            }

            DevicesCleared?.Invoke();
            return new DeviceTrustActionResult
            {
                Success = true,
                Message = removed <= 0 ? "No paired devices." : $"{removed} paired device(s) removed."
            };
        }

        private TrustedDeviceRecord FindTrustedDevice(string deviceToken)
        {
            if (string.IsNullOrWhiteSpace(deviceToken))
            {
                return null;
            }

            foreach (var device in devices)
            {
                if (!deviceStore.TryMatchTokenHash(device.TokenHash, deviceToken, out var strengthenedHash))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(strengthenedHash))
                {
                    // Opportunistic strengthening: once a legacy record proves knowledge of
                    // its token, replace the unkeyed SHA-256 hash with the keyed form.
                    device.TokenHash = strengthenedHash;
                    devicesDirty = true;
                }

                return device;
            }

            return null;
        }

        private static TrustedDeviceRecord CloneDevice(TrustedDeviceRecord device)
        {
            return new TrustedDeviceRecord
            {
                DeviceId = device.DeviceId,
                Name = device.Name,
                Model = device.Model,
                ClientInstanceId = device.ClientInstanceId,
                TokenHash = device.TokenHash,
                CreatedAt = device.CreatedAt,
                LastSeenAt = device.LastSeenAt,
                LastRemoteAddress = device.LastRemoteAddress,
                LastUserAgent = device.LastUserAgent
            };
        }

        private void TouchDevice(TrustedDeviceRecord device, string remoteAddress, string userAgent)
        {
            device.LastSeenAt = DateTimeOffset.UtcNow;
            device.LastRemoteAddress = remoteAddress;
            device.LastUserAgent = userAgent;
            devicesDirty = true;
            try
            {
                // Last-seen persistence is telemetry, not authorization. A
                // damaged ProgramData ACL must never turn a valid device-token
                // check into HTTP 500 and lock every paired device out.
                SaveDevices(force: false);
            }
            catch (IOException)
            {
                // Keep devicesDirty=true so a later request can retry.
            }
            catch (UnauthorizedAccessException)
            {
                // Setup repairs the ACL; trust remains valid in the meantime.
            }
        }

        private void UpdateDeviceIdentity(TrustedDeviceRecord device, string model, string clientInstanceId, string userAgent)
        {
            var changed = DeviceIdentityPolicy.ApplyIdentity(devices, device, model, clientInstanceId, userAgent);

            if (!changed)
            {
                return;
            }

            devicesDirty = true;
            try
            {
                SaveDevices(force: true);
            }
            catch (IOException)
            {
                // Identity enrichment must never invalidate an otherwise trusted device.
            }
            catch (UnauthorizedAccessException)
            {
                // Setup repairs ProgramData ACLs; retry through a later status request.
            }
        }

        private void SaveDevices(bool force)
        {
            if (!force && !devicesDirty)
            {
                return;
            }

            if (!force && DateTimeOffset.UtcNow - lastDevicesFlushAt < LastSeenSaveDebounce)
            {
                return;
            }

            if (deviceStore.PersistenceDisabled)
            {
                // Never clobber a store whose MAC key belongs to another Windows account
                // (or persist MACs no future process could verify). Trust stays valid
                // in memory for this session.
                devicesDirty = false;
                lastDevicesFlushAt = DateTimeOffset.UtcNow;
                return;
            }

            deviceStore.Save(devices);
            devicesDirty = false;
            lastDevicesFlushAt = DateTimeOffset.UtcNow;
        }

    }

    public sealed class PairingApprovalStartRequest
    {
        public string Name { get; set; }
        public string Platform { get; set; }
        public string Model { get; set; }
        public string ClientInstanceId { get; set; }
    }

    public sealed class PairingApprovalActionRequest { public string RequestId { get; set; } }
    public sealed class PairingApprovalPollRequest { public string RequestId { get; set; } public string RequestSecret { get; set; } }

    public sealed class PendingApprovalPairing
    {
        public string RequestId { get; set; }
        public string RequestSecretHash { get; set; }
        public string RequestSecret { get; set; }
        public string Name { get; set; }
        public string Platform { get; set; }
        public string Model { get; set; }
        public string ClientInstanceId { get; set; }
        public string RemoteAddress { get; set; }
        public string UserAgent { get; set; }
        public string VerificationCode { get; set; }
        public string Status { get; set; }
        public string DeviceId { get; set; }
        public string DeviceToken { get; set; }
        public bool Continued { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }

    public sealed class PairingApprovalRequestResult
    {
        public bool Success { get; set; } = true;
        public string RequestId { get; set; }
        public string RequestSecret { get; set; }
        public string VerificationCode { get; set; }
        public string Status { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public static PairingApprovalRequestResult From(PendingApprovalPairing value, string secret) => new PairingApprovalRequestResult
        { RequestId = value.RequestId, RequestSecret = secret, VerificationCode = value.VerificationCode, Status = value.Status, ExpiresAt = value.ExpiresAt };
    }

    public sealed class PairingApprovalSummary
    {
        public string RequestId { get; set; }
        public string Name { get; set; }
        public string Platform { get; set; }
        public string RemoteAddress { get; set; }
        public string VerificationCode { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public static PairingApprovalSummary From(PendingApprovalPairing value) => new PairingApprovalSummary
        { RequestId = value.RequestId, Name = value.Name, Platform = value.Platform, RemoteAddress = value.RemoteAddress, VerificationCode = value.VerificationCode, ExpiresAt = value.ExpiresAt };
    }

    public sealed class PairingApprovalPollResult
    {
        public bool Success { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public string DeviceId { get; set; }
        public string DeviceToken { get; set; }
        public string DeviceName { get; set; }
        public bool Continued { get; set; }
        public static PairingApprovalPollResult Fail(string message) => new PairingApprovalPollResult { Success = false, Status = "expired", Message = message };
    }

    public sealed class DeviceRevokeRequest
    {
        public string DeviceId { get; set; }
    }

    public sealed class DeviceTrustActionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }

        public static DeviceTrustActionResult Fail(string message)
        {
            return new DeviceTrustActionResult
            {
                Success = false,
                Message = message
            };
        }
    }

    public sealed class DeviceTrustStatus
    {
        public bool Trusted { get; set; }
        public bool LocalRequest { get; set; }
        public string DeviceHeader { get; set; }
        public int PairedDeviceCount { get; set; }
        public DeviceSummary CurrentDevice { get; set; }
        public List<DeviceSummary> Devices { get; set; }
    }

    public sealed class DeviceSummary
    {
        private static readonly TimeSpan ConnectedWindow = TimeSpan.FromSeconds(25);

        public string DeviceId { get; set; }
        public string Name { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastSeenAt { get; set; }
        public string LastRemoteAddress { get; set; }
        public bool Connected { get; set; }

        public static DeviceSummary From(TrustedDeviceRecord device, DateTimeOffset? now = null)
        {
            var observedAt = now ?? DateTimeOffset.UtcNow;
            return new DeviceSummary
            {
                DeviceId = device.DeviceId,
                Name = device.Name,
                CreatedAt = device.CreatedAt,
                LastSeenAt = device.LastSeenAt,
                LastRemoteAddress = device.LastRemoteAddress,
                Connected = observedAt - device.LastSeenAt <= ConnectedWindow
            };
        }
    }

    public sealed class TrustedDeviceRecord
    {
        public string DeviceId { get; set; }
        public string Name { get; set; }
        public string Model { get; set; }
        public string ClientInstanceId { get; set; }
        public string TokenHash { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastSeenAt { get; set; }
        public string LastRemoteAddress { get; set; }
        public string LastUserAgent { get; set; }
    }
}
