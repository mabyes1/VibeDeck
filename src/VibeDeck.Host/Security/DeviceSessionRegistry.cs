using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace VibeDeck.Host.Security
{
    /// <summary>
    /// Tracks the live display/input sessions belonging to each trusted device so that
    /// revoking a device actually ends what it is already doing.
    ///
    /// Trust is checked once, when a WebSocket is accepted; the loops that follow apply
    /// every frame and every keystroke without asking again. Without this registry,
    /// revoking a device only stops it from starting something NEW — a phone that is
    /// already holding /ws/input open keeps driving the mouse and keyboard until it
    /// chooses to disconnect, which is exactly the case revocation exists for.
    /// </summary>
    public sealed class DeviceSessionRegistry
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, CancellationTokenSource>> sessions =
            new ConcurrentDictionary<string, ConcurrentDictionary<long, CancellationTokenSource>>(StringComparer.Ordinal);
        private long nextSessionId;

        /// <summary>Number of devices currently holding at least one live session.</summary>
        public int TrackedDevices => sessions.Count;

        public int SessionCountFor(string deviceId)
        {
            return !string.IsNullOrEmpty(deviceId) && sessions.TryGetValue(deviceId, out var bucket)
                ? bucket.Count
                : 0;
        }

        /// <summary>
        /// Registers a live session. Dispose the handle when the session ends; the token
        /// exposed by the handle is cancelled if the device is revoked in the meantime.
        /// </summary>
        public DeviceSessionHandle Open(string deviceId, CancellationToken requestAborted)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
            if (string.IsNullOrEmpty(deviceId))
            {
                // Local console / host-authenticated callers have no device identity to
                // revoke. They still get a handle so callers need no special-casing.
                return new DeviceSessionHandle(this, null, 0, linked);
            }

            var id = Interlocked.Increment(ref nextSessionId);
            var bucket = sessions.GetOrAdd(deviceId, _ => new ConcurrentDictionary<long, CancellationTokenSource>());
            bucket[id] = linked;

            // GetOrAdd may have raced with CancelDevice removing the bucket; if the bucket
            // we just filled is no longer the published one, this device was revoked in
            // between, so honour that rather than leaving an untracked live session.
            if (!sessions.TryGetValue(deviceId, out var current) || !ReferenceEquals(current, bucket))
            {
                Cancel(linked);
            }

            return new DeviceSessionHandle(this, deviceId, id, linked);
        }

        /// <summary>Ends every live session for one device. Returns how many were closed.</summary>
        public int CancelDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId) || !sessions.TryRemove(deviceId, out var bucket))
            {
                return 0;
            }

            var closed = 0;
            foreach (var entry in bucket)
            {
                Cancel(entry.Value);
                closed++;
            }

            return closed;
        }

        /// <summary>Ends every live session for every device (pairings cleared / host logout).</summary>
        public int CancelAll()
        {
            var closed = 0;
            foreach (var deviceId in new List<string>(sessions.Keys))
            {
                closed += CancelDevice(deviceId);
            }

            return closed;
        }

        private static void Cancel(CancellationTokenSource source)
        {
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The session ended on its own between removal and cancellation.
            }
        }

        private void Release(string deviceId, long sessionId)
        {
            if (string.IsNullOrEmpty(deviceId) || !sessions.TryGetValue(deviceId, out var bucket))
            {
                return;
            }

            bucket.TryRemove(sessionId, out _);
            if (bucket.IsEmpty)
            {
                // Best-effort tidy-up. A racing Open may re-add concurrently, which the
                // bucket-identity check in Open detects.
                sessions.TryRemove(new KeyValuePair<string, ConcurrentDictionary<long, CancellationTokenSource>>(deviceId, bucket));
            }
        }

        public sealed class DeviceSessionHandle : IDisposable
        {
            private readonly DeviceSessionRegistry registry;
            private readonly string deviceId;
            private readonly long sessionId;
            private readonly CancellationTokenSource source;
            private int disposed;

            internal DeviceSessionHandle(
                DeviceSessionRegistry registry,
                string deviceId,
                long sessionId,
                CancellationTokenSource source)
            {
                this.registry = registry;
                this.deviceId = deviceId;
                this.sessionId = sessionId;
                this.source = source;
            }

            /// <summary>Cancelled when the request aborts OR the device is revoked.</summary>
            public CancellationToken Token => source.Token;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0)
                {
                    return;
                }

                registry.Release(deviceId, sessionId);
                source.Dispose();
            }
        }
    }
}
