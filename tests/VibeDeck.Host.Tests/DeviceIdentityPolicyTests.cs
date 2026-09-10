using System;
using System.Collections.Generic;

using VibeDeck.Host.Security;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class DeviceIdentityPolicyTests
    {
        [Theory]
        [InlineData("Phone", "GoColor7", "Mozilla/5.0 Android", "BOOX Go Color 7")]
        [InlineData("Phone", "BOOX Go Color 7", "Mozilla/5.0 Android", "BOOX Go Color 7")]
        [InlineData("Phone", "sm-s9110", "Mozilla/5.0 Android", "Samsung SM-S9110")]
        [InlineData("My Reader", "", "Mozilla/5.0", "My Reader")]
        [InlineData("Phone", "", "Mozilla/5.0 Android", "Android device")]
        [InlineData("Phone", "", "Mozilla/5.0 iPad", "iPad")]
        [InlineData("Phone", "", "Mozilla/5.0 iPhone", "iPhone")]
        public void ResolveNamePrefersModelThenUsesPlatformNeutralFallback(string name, string model, string userAgent, string expected)
        {
            Assert.Equal(expected, DeviceIdentityPolicy.ResolveName(name, model, userAgent));
        }

        [Fact]
        public void NormalizeIdentifiersRejectsLegacyNoise()
        {
            Assert.Equal("", DeviceIdentityPolicy.NormalizeModel("Unknown"));
            Assert.Equal("", DeviceIdentityPolicy.NormalizeModel("K"));
            Assert.Equal("SM-S9110", DeviceIdentityPolicy.NormalizeModel(" SM-S9110 "));
            Assert.Equal("", DeviceIdentityPolicy.NormalizeClientInstanceId("too-short"));
            Assert.Equal("1234567890abcdef", DeviceIdentityPolicy.NormalizeClientInstanceId(" 1234567890abcdef "));
        }

        [Fact]
        public void NormalizeRecordsKeepsNewestStableBrowserIdentity()
        {
            var records = new List<TrustedDeviceRecord>
            {
                Record("old", "instance-123456789", "2026-08-01T00:00:00Z"),
                Record("new", "instance-123456789", "2026-08-08T00:00:00Z"),
            };

            var changed = DeviceIdentityPolicy.NormalizeRecords(records);

            Assert.True(changed);
            Assert.Single(records);
            Assert.Equal("new", records[0].DeviceId);
        }

        [Fact]
        public void NormalizeRecordsDeduplicatesLegacyExactAddressAndUserAgentOnly()
        {
            var records = new List<TrustedDeviceRecord>
            {
                Record("old", "", "2026-08-01T00:00:00Z", "10.0.0.2", "same-agent"),
                Record("new", "", "2026-08-08T00:00:00Z", "10.0.0.2", "same-agent"),
                Record("other", "", "2026-08-09T00:00:00Z", "10.0.0.3", "same-agent"),
            };

            Assert.True(DeviceIdentityPolicy.NormalizeRecords(records));
            Assert.Equal(2, records.Count);
            Assert.Contains(records, record => record.DeviceId == "new");
            Assert.Contains(records, record => record.DeviceId == "other");
        }

        [Fact]
        public void FindPairingContinuationPrefersStableClientInstance()
        {
            var records = new[]
            {
                Record("older", "1234567890abcdef", "2026-08-01T00:00:00Z"),
                Record("newer", "1234567890abcdef", "2026-08-08T00:00:00Z"),
            };
            var request = new PendingApprovalPairing { ClientInstanceId = "1234567890abcdef" };

            var match = DeviceIdentityPolicy.FindPairingContinuation(records, request);

            Assert.Equal("newer", match.DeviceId);
        }

        [Fact]
        public void ApplyIdentityEnrichesAndRemovesDuplicateBrowserRecord()
        {
            var current = Record("current", "", "2026-08-08T00:00:00Z");
            current.Name = "Phone";
            var duplicate = Record("duplicate", "1234567890abcdef", "2026-08-07T00:00:00Z");
            var records = new List<TrustedDeviceRecord> { current, duplicate };

            var changed = DeviceIdentityPolicy.ApplyIdentity(
                records,
                current,
                "sm-s9110",
                "1234567890abcdef",
                "Mozilla/5.0 Android");

            Assert.True(changed);
            Assert.Single(records);
            Assert.Equal("sm-s9110", current.Model);
            Assert.Equal("Samsung SM-S9110", current.Name);
            Assert.Equal("1234567890abcdef", current.ClientInstanceId);
        }

        private static TrustedDeviceRecord Record(
            string id,
            string clientInstanceId,
            string lastSeen,
            string address = "",
            string userAgent = "")
        {
            return new TrustedDeviceRecord
            {
                DeviceId = id,
                Name = "Phone",
                ClientInstanceId = clientInstanceId,
                LastSeenAt = DateTimeOffset.Parse(lastSeen),
                LastRemoteAddress = address,
                LastUserAgent = userAgent
            };
        }
    }
}
