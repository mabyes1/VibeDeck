using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace VibeDeck.Host.Security
{
    /// <summary>
    /// Short-lived, single-use tickets for authenticating WebSocket handshakes.
    ///
    /// Browsers cannot set headers on a WebSocket handshake, so the client used to put the
    /// persistent device token straight into the URL. URLs are routinely written to CDN and
    /// proxy access logs, which meant a single logged /ws/input line handed over a
    /// 400-day credential for the PC's keyboard and mouse.
    ///
    /// A ticket is obtained over a normal header-authenticated request and dies within
    /// seconds of being redeemed, so the value that does appear in the URL is worthless by
    /// the time anyone reads it back out of a log.
    /// </summary>
    public sealed class WebSocketTicketService
    {
        /// <summary>Long enough for the client to open the socket, far too short to harvest.</summary>
        public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

        /// <summary>Bounds memory if a paired-but-misbehaving client mints without connecting.</summary>
        public const int MaxOutstanding = 512;

        private readonly ConcurrentDictionary<string, Ticket> tickets =
            new ConcurrentDictionary<string, Ticket>(StringComparer.Ordinal);

        public int Outstanding => tickets.Count;

        /// <summary>Mints a ticket for an already-authenticated device.</summary>
        public string Issue(string deviceId, DateTimeOffset now)
        {
            PruneExpired(now);
            if (tickets.Count >= MaxOutstanding)
            {
                // Drop the oldest rather than refuse: a legitimate reconnect must not be
                // blocked by stale, never-redeemed tickets.
                DropOldest();
            }

            var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            tickets[value] = new Ticket(deviceId, now + Lifetime);
            return value;
        }

        /// <summary>
        /// Redeems a ticket. Succeeds at most once per ticket; a replayed value from a log
        /// finds nothing. <paramref name="deviceId"/> may be null for the local console,
        /// which has no device identity, so callers must use the return value to decide.
        /// </summary>
        public bool TryRedeem(string ticket, DateTimeOffset now, out string deviceId)
        {
            deviceId = null;
            if (string.IsNullOrWhiteSpace(ticket) || !tickets.TryRemove(ticket, out var entry))
            {
                return false;
            }

            if (entry.ExpiresAt <= now)
            {
                return false;
            }

            deviceId = entry.DeviceId;
            return true;
        }

        private void PruneExpired(DateTimeOffset now)
        {
            foreach (var pair in tickets)
            {
                if (pair.Value.ExpiresAt <= now)
                {
                    tickets.TryRemove(pair.Key, out _);
                }
            }
        }

        private void DropOldest()
        {
            string oldestKey = null;
            var oldest = DateTimeOffset.MaxValue;
            foreach (var pair in tickets)
            {
                if (pair.Value.ExpiresAt < oldest)
                {
                    oldest = pair.Value.ExpiresAt;
                    oldestKey = pair.Key;
                }
            }

            if (oldestKey != null)
            {
                tickets.TryRemove(oldestKey, out _);
            }
        }

        private sealed class Ticket
        {
            public Ticket(string deviceId, DateTimeOffset expiresAt)
            {
                DeviceId = deviceId;
                ExpiresAt = expiresAt;
            }

            public string DeviceId { get; }
            public DateTimeOffset ExpiresAt { get; }
        }
    }
}
