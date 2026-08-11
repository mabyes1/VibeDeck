using System;
using System.Threading;
using System.Threading.Tasks;
using VibeDeck.Host.Security;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class DeviceSessionRegistryTests
    {
        [Fact]
        public void RevokingADeviceCancelsItsLiveSessions()
        {
            var registry = new DeviceSessionRegistry();
            using var handle = registry.Open("device-a", CancellationToken.None);

            Assert.False(handle.Token.IsCancellationRequested);

            var closed = registry.CancelDevice("device-a");

            Assert.Equal(1, closed);
            Assert.True(handle.Token.IsCancellationRequested, "a revoked device's session must be cancelled");
        }

        [Fact]
        public void RevokingOneDeviceLeavesOtherDevicesRunning()
        {
            var registry = new DeviceSessionRegistry();
            using var a = registry.Open("device-a", CancellationToken.None);
            using var b = registry.Open("device-b", CancellationToken.None);

            registry.CancelDevice("device-a");

            Assert.True(a.Token.IsCancellationRequested);
            Assert.False(b.Token.IsCancellationRequested, "revoking one phone must not disconnect the others");
        }

        [Fact]
        public void AllOfADevicesSessionsAreCancelledNotJustTheFirst()
        {
            // A phone typically holds display AND input open at the same time. Cancelling
            // only one would leave it still able to drive the mouse and keyboard.
            var registry = new DeviceSessionRegistry();
            using var display = registry.Open("device-a", CancellationToken.None);
            using var input = registry.Open("device-a", CancellationToken.None);

            Assert.Equal(2, registry.SessionCountFor("device-a"));
            var closed = registry.CancelDevice("device-a");

            Assert.Equal(2, closed);
            Assert.True(display.Token.IsCancellationRequested);
            Assert.True(input.Token.IsCancellationRequested);
        }

        [Fact]
        public void ClearingAllPairingsCancelsEveryDevice()
        {
            var registry = new DeviceSessionRegistry();
            using var a = registry.Open("device-a", CancellationToken.None);
            using var b = registry.Open("device-b", CancellationToken.None);
            using var c = registry.Open("device-c", CancellationToken.None);

            var closed = registry.CancelAll();

            Assert.Equal(3, closed);
            Assert.True(a.Token.IsCancellationRequested);
            Assert.True(b.Token.IsCancellationRequested);
            Assert.True(c.Token.IsCancellationRequested);
            Assert.Equal(0, registry.TrackedDevices);
        }

        [Fact]
        public void AnAbortedRequestStillCancelsItsSession()
        {
            var registry = new DeviceSessionRegistry();
            using var aborted = new CancellationTokenSource();
            using var handle = registry.Open("device-a", aborted.Token);

            aborted.Cancel();

            Assert.True(handle.Token.IsCancellationRequested, "the request-abort token must still propagate");
        }

        [Fact]
        public void DisposingASessionStopsTrackingIt()
        {
            var registry = new DeviceSessionRegistry();
            using (registry.Open("device-a", CancellationToken.None))
            {
                Assert.Equal(1, registry.SessionCountFor("device-a"));
            }

            Assert.Equal(0, registry.SessionCountFor("device-a"));
            Assert.Equal(0, registry.TrackedDevices);
            Assert.Equal(0, registry.CancelDevice("device-a"));
        }

        [Fact]
        public void DisposingOneSessionDoesNotUntrackTheDevicesOtherSessions()
        {
            var registry = new DeviceSessionRegistry();
            var first = registry.Open("device-a", CancellationToken.None);
            using var second = registry.Open("device-a", CancellationToken.None);

            first.Dispose();

            Assert.Equal(1, registry.SessionCountFor("device-a"));
            Assert.Equal(1, registry.CancelDevice("device-a"));
            Assert.True(second.Token.IsCancellationRequested);
        }

        [Fact]
        public void CallersWithoutADeviceIdentityAreUnaffectedByRevocation()
        {
            // The local console authenticates without a device token; it has no pairing to
            // revoke and must not be torn down when a phone is.
            var registry = new DeviceSessionRegistry();
            using var local = registry.Open(null, CancellationToken.None);

            registry.CancelAll();

            Assert.False(local.Token.IsCancellationRequested);
            Assert.Equal(0, registry.TrackedDevices);
        }

        [Fact]
        public void DisposeIsIdempotentAndSafeAfterRevocation()
        {
            var registry = new DeviceSessionRegistry();
            var handle = registry.Open("device-a", CancellationToken.None);

            registry.CancelDevice("device-a");
            handle.Dispose();
            handle.Dispose();

            Assert.Equal(0, registry.SessionCountFor("device-a"));
        }

        [Fact]
        public void ConcurrentOpensAndRevocationsStaySafeAndLeakNothing()
        {
            var registry = new DeviceSessionRegistry();
            var opened = 0;

            Parallel.For(0, 400, index =>
            {
                var deviceId = "device-" + (index % 8);
                using var handle = registry.Open(deviceId, CancellationToken.None);
                Interlocked.Increment(ref opened);
                if (index % 5 == 0)
                {
                    registry.CancelDevice(deviceId);
                }
            });

            Assert.Equal(400, opened);
            // Every handle above was disposed, so nothing may remain tracked.
            Assert.Equal(0, registry.TrackedDevices);
        }
    }
}
