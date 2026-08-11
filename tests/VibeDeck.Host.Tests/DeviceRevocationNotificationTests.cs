using System;
using System.Collections.Generic;
using System.IO;
using VibeDeck.Host.Security;
using Xunit;

namespace VibeDeck.Host.Tests
{
    /// <summary>
    /// The trust store must announce revocation, otherwise live display/input sessions
    /// keep running after the owner has revoked the phone.
    /// </summary>
    public sealed class DeviceRevocationNotificationTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "VibeDeck-tests", Guid.NewGuid().ToString("N"));

        private static string PairDevice(DeviceTrustService service, string clientId, out string token)
        {
            const string userAgent = "Mozilla/5.0 (Linux; Android 10; K) Chrome/150 Mobile";
            var request = service.RequestApproval("Android 裝置", "Android", "SM-S9110", clientId, userAgent, "192.168.0.24");
            Assert.True(service.ApproveRequest(request.RequestId).Success);
            var poll = service.PollApproval(request.RequestId, request.RequestSecret);
            token = poll.DeviceToken;
            return poll.DeviceId;
        }

        [Fact]
        public void Revoking_a_device_announces_that_device_id()
        {
            var service = new DeviceTrustService(root);
            var deviceId = PairDevice(service, "browser-instance-1234567890", out _);

            var announced = new List<string>();
            service.DeviceRevoked += id => announced.Add(id);

            Assert.True(service.RevokeDevice(deviceId).Success);

            Assert.Equal(new[] { deviceId }, announced);
        }

        [Fact]
        public void A_failed_revoke_announces_nothing()
        {
            var service = new DeviceTrustService(root);
            PairDevice(service, "browser-instance-1234567890", out _);

            var announced = 0;
            service.DeviceRevoked += _ => announced++;

            Assert.False(service.RevokeDevice("no-such-device").Success);
            Assert.False(service.RevokeDevice("").Success);

            Assert.Equal(0, announced);
        }

        [Fact]
        public void Clearing_pairings_announces_once()
        {
            var service = new DeviceTrustService(root);
            PairDevice(service, "browser-instance-1234567890", out _);
            PairDevice(service, "browser-instance-0987654321", out _);

            var cleared = 0;
            service.DevicesCleared += () => cleared++;

            Assert.True(service.ClearDevices().Success);

            Assert.Equal(1, cleared);
        }

        [Fact]
        public void Resolving_a_device_id_from_its_token_identifies_the_session_owner()
        {
            // The streaming endpoints need this to know whose sessions to cancel later.
            var service = new DeviceTrustService(root);
            var deviceId = PairDevice(service, "browser-instance-1234567890", out var token);

            Assert.Equal(deviceId, service.ResolveDeviceId(token));
            Assert.Null(service.ResolveDeviceId("not-a-real-token"));
            Assert.Null(service.ResolveDeviceId(null));
        }

        [Fact]
        public void A_revoked_device_id_stops_being_trusted()
        {
            // Backs the periodic re-check inside the input loop.
            var service = new DeviceTrustService(root);
            var deviceId = PairDevice(service, "browser-instance-1234567890", out _);

            Assert.True(service.IsDeviceTrusted(deviceId));
            service.RevokeDevice(deviceId);

            Assert.False(service.IsDeviceTrusted(deviceId));
            Assert.False(service.IsDeviceTrusted(null));
        }

        [Fact]
        public void Revocation_reaches_a_session_registry_wired_to_the_event()
        {
            // End to end over the real seam: pair, open a session, revoke, and the
            // session's token must be cancelled.
            var service = new DeviceTrustService(root);
            var registry = new DeviceSessionRegistry();
            service.DeviceRevoked += id => registry.CancelDevice(id);
            service.DevicesCleared += () => registry.CancelAll();

            var deviceId = PairDevice(service, "browser-instance-1234567890", out _);
            using var session = registry.Open(deviceId, System.Threading.CancellationToken.None);
            Assert.False(session.Token.IsCancellationRequested);

            service.RevokeDevice(deviceId);

            Assert.True(session.Token.IsCancellationRequested, "revocation must reach the live session");
        }

        public void Dispose()
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
