using System;
using VibeDeck.Host.Security;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class RequestRateLimiterTests
    {
        private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-07-26T10:00:00+00:00");

        [Fact]
        public void BurstIsAllowedThenExhausted()
        {
            var limiter = new RequestRateLimiter();
            var rule = new RateRule(burst: 5, perMinute: 60);

            for (var i = 0; i < 5; i++)
            {
                Assert.True(limiter.TryAcquire("k", rule, Start, out _), $"permit {i} should be allowed");
            }

            Assert.False(limiter.TryAcquire("k", rule, Start, out var retryAfter));
            Assert.True(retryAfter > 0, "a rejected caller must be told how long to wait");
        }

        [Fact]
        public void TokensRefillOverTimeUpToBurst()
        {
            var limiter = new RequestRateLimiter();
            var rule = new RateRule(burst: 5, perMinute: 60); // 1 per second

            for (var i = 0; i < 5; i++)
            {
                limiter.TryAcquire("k", rule, Start, out _);
            }
            Assert.False(limiter.TryAcquire("k", rule, Start, out _));

            // Two seconds later exactly two permits are back, not more.
            Assert.True(limiter.TryAcquire("k", rule, Start.AddSeconds(2), out _));
            Assert.True(limiter.TryAcquire("k", rule, Start.AddSeconds(2), out _));
            Assert.False(limiter.TryAcquire("k", rule, Start.AddSeconds(2), out _));

            // After a long idle period the bucket is capped at burst, never above.
            for (var i = 0; i < 5; i++)
            {
                Assert.True(limiter.TryAcquire("k", rule, Start.AddHours(1), out _));
            }
            Assert.False(limiter.TryAcquire("k", rule, Start.AddHours(1), out _));
        }

        [Fact]
        public void KeysAreIsolatedSoOneClientCannotStarveAnother()
        {
            var limiter = new RequestRateLimiter();
            var rule = new RateRule(burst: 2, perMinute: 60);

            Assert.True(limiter.TryAcquire("client-a", rule, Start, out _));
            Assert.True(limiter.TryAcquire("client-a", rule, Start, out _));
            Assert.False(limiter.TryAcquire("client-a", rule, Start, out _));

            Assert.True(limiter.TryAcquire("client-b", rule, Start, out _));
            Assert.True(limiter.TryAcquire("client-b", rule, Start, out _));
        }

        [Fact]
        public void GlobalBucketStopsAnAttackerRotatingSourceAddresses()
        {
            // The per-address lockout is blind to IP rotation; the shared bucket is what
            // actually bounds a distributed brute force. Each request uses a fresh address
            // but they all draw on one global key.
            var limiter = new RequestRateLimiter();
            var perClient = new RateRule(burst: 6, perMinute: 12);
            var global = new RateRule(burst: 15, perMinute: 40);

            var allowed = 0;
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var rotatingAddress = $"host-login|203.0.113.{attempt % 256}";
                if (limiter.TryAcquire(rotatingAddress, perClient, Start, out _) &&
                    limiter.TryAcquire("host-login|*", global, Start, out _))
                {
                    allowed++;
                }
            }

            Assert.Equal(15, allowed);
        }

        [Fact]
        public void TrackedBucketsStayBoundedUnderAddressRotation()
        {
            var limiter = new RequestRateLimiter();
            var rule = new RateRule(burst: 1, perMinute: 1);

            for (var i = 0; i < RequestRateLimiter.MaxTrackedBuckets + 5000; i++)
            {
                limiter.TryAcquire($"key-{i}", rule, Start, out _);
            }

            Assert.True(
                limiter.TrackedBuckets <= RequestRateLimiter.MaxTrackedBuckets + 1,
                $"bucket table must stay bounded, saw {limiter.TrackedBuckets}");
        }

        [Fact]
        public void OverflowCallersAreStillThrottledRatherThanWavedThrough()
        {
            var limiter = new RequestRateLimiter();
            var rule = new RateRule(burst: 1, perMinute: 1);

            for (var i = 0; i < RequestRateLimiter.MaxTrackedBuckets; i++)
            {
                limiter.TryAcquire($"key-{i}", rule, Start, out _);
            }

            // Past the cap, unknown keys share one bucket: the first gets its permit, the
            // rest are refused. Filling the table must not become a way to bypass limiting.
            Assert.True(limiter.TryAcquire("overflow-a", rule, Start, out _));
            Assert.False(limiter.TryAcquire("overflow-b", rule, Start, out _));
        }

        [Fact]
        public void IdleFullBucketsAreReclaimedSoLegitimateClientsAreNotPushedToOverflow()
        {
            var limiter = new RequestRateLimiter();
            var rule = new RateRule(burst: 2, perMinute: 60);

            for (var i = 0; i < RequestRateLimiter.MaxTrackedBuckets; i++)
            {
                limiter.TryAcquire($"key-{i}", rule, Start, out _);
            }
            var beforePrune = limiter.TrackedBuckets;

            // An hour later every bucket above has refilled to capacity, so they carry no
            // state and are safe to evict. A newcomer must get its own bucket, not overflow.
            var later = Start.AddHours(1);
            limiter.TryAcquire("newcomer", rule, later, out _);

            Assert.True(limiter.TrackedBuckets < beforePrune, "fully refilled buckets should be reclaimed");
            Assert.True(limiter.TryAcquire("newcomer", rule, later, out _));
        }
    }
}
