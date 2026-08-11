using System;
using System.IO;
using VibeDeck.Host.Security;
using Xunit;

namespace VibeDeck.Host.Tests
{
    // Guardrail tests that pin the *current* pairing state-machine behavior of
    // DeviceTrustService before the auth/pairing vertical is refactored out of
    // Startup.cs. Service-level (no HTTP host), matching the project's existing
    // test style. Any behavior drift (deny path, poll one-shot semantics, pending
    // list, revoke/clear, token-based trust) should turn these red.
    public sealed class DeviceTrustPairingFlowTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "VibeDeck-tests", Guid.NewGuid().ToString("N"));

        private const string UserAgent = "Mozilla/5.0 (Linux; Android 10; K) Chrome/150 Mobile";
        private const string Address = "192.168.0.24";

        private DeviceTrustService NewService() => new DeviceTrustService(root);

        private PairingApprovalRequestResult Request(DeviceTrustService service, string clientId)
            => service.RequestApproval("Android 裝置", "Android", "SM-S9110", clientId, UserAgent, Address);

        [Fact]
        public void Poll_before_decision_returns_pending_without_token()
        {
            var service = NewService();
            var request = Request(service, "client-instance-aaaaaaaaaa");

            var poll = service.PollApproval(request.RequestId, request.RequestSecret);

            Assert.True(poll.Success);
            Assert.Equal("pending", poll.Status);
            Assert.Null(poll.DeviceToken);
        }

        [Fact]
        public void Poll_with_wrong_secret_fails()
        {
            var service = NewService();
            var request = Request(service, "client-instance-bbbbbbbbbb");

            var poll = service.PollApproval(request.RequestId, "not-the-secret");

            Assert.False(poll.Success);
        }

        [Fact]
        public void Deny_marks_denied_and_device_never_trusted()
        {
            var service = NewService();
            var request = Request(service, "client-instance-cccccccccc");

            Assert.True(service.DenyRequest(request.RequestId).Success);
            var poll = service.PollApproval(request.RequestId, request.RequestSecret);

            Assert.True(poll.Success);
            Assert.Equal("denied", poll.Status);
            Assert.Null(poll.DeviceToken);
            Assert.Equal(0, service.GetStatus(null, "127.0.0.1", "test", true, false).PairedDeviceCount);
        }

        [Fact]
        public void Approved_poll_is_one_shot()
        {
            var service = NewService();
            var request = Request(service, "client-instance-dddddddddd");
            Assert.True(service.ApproveRequest(request.RequestId).Success);

            var first = service.PollApproval(request.RequestId, request.RequestSecret);
            var second = service.PollApproval(request.RequestId, request.RequestSecret);

            Assert.True(first.Success);
            Assert.Equal("approved", first.Status);
            Assert.False(string.IsNullOrEmpty(first.DeviceToken));
            Assert.False(second.Success); // pending entry is consumed after an approved/denied poll
        }

        [Fact]
        public void Denied_poll_is_one_shot()
        {
            var service = NewService();
            var request = Request(service, "client-instance-eeeeeeeeee");
            Assert.True(service.DenyRequest(request.RequestId).Success);

            var first = service.PollApproval(request.RequestId, request.RequestSecret);
            var second = service.PollApproval(request.RequestId, request.RequestSecret);

            Assert.True(first.Success);
            Assert.Equal("denied", first.Status);
            Assert.False(second.Success);
        }

        [Fact]
        public void Pending_list_tracks_requests_and_clears_after_a_decision()
        {
            var service = NewService();
            var a = Request(service, "client-instance-ff11111111");
            var b = Request(service, "client-instance-ff22222222");

            Assert.Equal(2, service.GetPendingApprovals().Count);

            Assert.True(service.ApproveRequest(a.RequestId).Success);
            Assert.True(service.DenyRequest(b.RequestId).Success);

            Assert.Empty(service.GetPendingApprovals());
        }

        [Fact]
        public void RequestApproval_is_idempotent_for_the_same_client()
        {
            var service = NewService();
            var first = Request(service, "client-instance-gggggggggg");
            var second = Request(service, "client-instance-gggggggggg");

            Assert.Equal(first.RequestId, second.RequestId);
            Assert.Single(service.GetPendingApprovals());
        }

        [Fact]
        public void Approve_unknown_or_already_decided_request_fails()
        {
            var service = NewService();
            var request = Request(service, "client-instance-hhhhhhhhhh");

            Assert.False(service.ApproveRequest("no-such-request").Success);
            Assert.True(service.ApproveRequest(request.RequestId).Success);
            Assert.False(service.ApproveRequest(request.RequestId).Success); // no longer pending
        }

        [Fact]
        public void IsTrusted_is_true_for_paired_token_and_false_otherwise()
        {
            var service = NewService();
            var request = Request(service, "client-instance-iiiiiiiiii");
            Assert.True(service.ApproveRequest(request.RequestId).Success);
            var poll = service.PollApproval(request.RequestId, request.RequestSecret);

            Assert.True(service.IsTrusted(poll.DeviceToken, Address, UserAgent));
            Assert.False(service.IsTrusted("bogus-token", Address, UserAgent));
            Assert.False(service.IsTrusted(null, Address, UserAgent));
        }

        [Fact]
        public void Revoke_removes_trust_for_that_device()
        {
            var service = NewService();
            var request = Request(service, "client-instance-jjjjjjjjjj");
            Assert.True(service.ApproveRequest(request.RequestId).Success);
            var poll = service.PollApproval(request.RequestId, request.RequestSecret);
            Assert.True(service.IsTrusted(poll.DeviceToken, Address, UserAgent));

            Assert.True(service.RevokeDevice(poll.DeviceId).Success);

            Assert.False(service.IsTrusted(poll.DeviceToken, Address, UserAgent));
            Assert.Equal(0, service.GetStatus(null, "127.0.0.1", "test", true, false).PairedDeviceCount);
        }

        [Fact]
        public void Revoke_unknown_or_blank_device_fails()
        {
            var service = NewService();

            Assert.False(service.RevokeDevice("no-such-device").Success);
            Assert.False(service.RevokeDevice("").Success);
        }

        [Fact]
        public void Clear_removes_all_devices_and_pending_requests()
        {
            var service = NewService();
            var request = Request(service, "client-instance-kkkkkkkkkk");
            Assert.True(service.ApproveRequest(request.RequestId).Success);
            var poll = service.PollApproval(request.RequestId, request.RequestSecret);
            Request(service, "client-instance-llllllllll"); // an extra pending request

            Assert.True(service.ClearDevices().Success);

            Assert.False(service.IsTrusted(poll.DeviceToken, Address, UserAgent));
            Assert.Equal(0, service.GetStatus(null, "127.0.0.1", "test", true, false).PairedDeviceCount);
            Assert.Empty(service.GetPendingApprovals());
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
