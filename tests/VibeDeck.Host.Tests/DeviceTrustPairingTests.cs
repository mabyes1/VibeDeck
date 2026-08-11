using System;
using System.IO;
using VibeDeck.Host.Security;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class DeviceTrustPairingTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "VibeDeck-tests", Guid.NewGuid().ToString("N"));

        [Fact]
        public void RePairing_same_browser_continues_record_and_rotates_token()
        {
            var service = new DeviceTrustService(root);
            const string clientId = "browser-instance-1234567890";
            const string userAgent = "Mozilla/5.0 (Linux; Android 10; K) Chrome/150 Mobile";
            const string address = "192.168.0.24";

            var first = service.RequestApproval("Android 裝置", "Android", "SM-S9110", clientId, userAgent, address);
            Assert.True(service.ApproveRequest(first.RequestId).Success);
            var firstPoll = service.PollApproval(first.RequestId, first.RequestSecret);

            Assert.False(firstPoll.Continued);
            Assert.Equal("Samsung SM-S9110", firstPoll.DeviceName);
            Assert.True(service.GetStatus(firstPoll.DeviceToken, address, userAgent, false, false).Trusted);

            var second = service.RequestApproval("Android 裝置", "Android", "SM-S9110", clientId, userAgent, address);
            Assert.True(service.ApproveRequest(second.RequestId).Success);
            var secondPoll = service.PollApproval(second.RequestId, second.RequestSecret);
            var localStatus = service.GetStatus(null, "127.0.0.1", "test", true, false);

            Assert.True(secondPoll.Continued);
            Assert.Equal(firstPoll.DeviceId, secondPoll.DeviceId);
            Assert.NotEqual(firstPoll.DeviceToken, secondPoll.DeviceToken);
            Assert.Equal(1, localStatus.PairedDeviceCount);
            Assert.False(service.GetStatus(firstPoll.DeviceToken, address, userAgent, false, false).Trusted);
            Assert.True(service.GetStatus(secondPoll.DeviceToken, address, userAgent, false, false).Trusted);
        }

        [Fact]
        public void Boox_model_gets_exact_product_name()
        {
            var service = new DeviceTrustService(root);
            var request = service.RequestApproval(
                "Android 裝置", "BOOX / 電子紙", "GoColor7", "boox-instance-1234567890",
                "Mozilla/5.0 (Linux; Android 10; K) Chrome/111", "192.168.0.15");

            Assert.True(service.ApproveRequest(request.RequestId).Success);
            var poll = service.PollApproval(request.RequestId, request.RequestSecret);

            Assert.Equal("BOOX Go Color 7", poll.DeviceName);
        }

        [Fact]
        public void Existing_trusted_device_is_enriched_without_repairing()
        {
            var service = new DeviceTrustService(root);
            const string userAgent = "Mozilla/5.0 (Linux; Android 10; K) Chrome/111";
            var request = service.RequestApproval(
                "Android 裝置", "Android", "", "", userAgent, "192.168.0.15");
            Assert.True(service.ApproveRequest(request.RequestId).Success);
            var poll = service.PollApproval(request.RequestId, request.RequestSecret);

            var status = service.GetStatus(
                poll.DeviceToken, "192.168.0.15", userAgent, false, false,
                "GoColor7", "boox-instance-1234567890");
            var restarted = new DeviceTrustService(root);
            var persisted = restarted.GetStatus(null, "127.0.0.1", "test", true, false);

            Assert.True(status.Trusted);
            Assert.Equal("BOOX Go Color 7", status.CurrentDevice.Name);
            Assert.Equal(1, persisted.PairedDeviceCount);
            Assert.Equal("BOOX Go Color 7", persisted.Devices[0].Name);

            File.WriteAllText(Path.Combine(root, "trusted-devices.json"), "{broken");
            var recovered = new DeviceTrustService(root)
                .GetStatus(null, "127.0.0.1", "test", true, false);
            Assert.Equal(1, recovered.PairedDeviceCount);
        }

        public void Dispose()
        {
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
