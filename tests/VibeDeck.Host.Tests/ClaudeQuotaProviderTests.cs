using System;
using System.Text.Json;
using VibeDeck.Host.Quotas;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class ClaudeQuotaProviderTests
    {
        private const string ObservedPayload = @"{
          ""five_hour"": { ""utilization"": 88.0, ""resets_at"": ""2026-07-26T16:10:00.097046+00:00"",
                           ""limit_dollars"": null, ""used_dollars"": null },
          ""seven_day"": null,
          ""seven_day_opus"": null,
          ""seven_day_sonnet"": null,
          ""extra_usage"": { ""is_enabled"": false, ""monthly_limit"": null, ""utilization"": null },
          ""limits"": [
            { ""kind"": ""session"", ""group"": ""session"", ""percent"": 88, ""severity"": ""warning"",
              ""resets_at"": ""2026-07-26T16:10:00.097046+00:00"", ""is_active"": true },
            { ""kind"": ""weekly_scoped"", ""group"": ""weekly"", ""percent"": 61, ""severity"": ""normal"",
              ""resets_at"": ""2026-07-31T01:00:00.097359+00:00"", ""is_active"": false }
          ]
        }";

        private static JsonElement Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        [Fact]
        public void ReadsTheFiveHourWindowFromItsNamedBucket()
        {
            var window = ClaudeQuotaProvider.ReadNamedWindow(Parse(ObservedPayload), "5h", 300, "five_hour");

            Assert.NotNull(window);
            Assert.Equal(88d, window.UsedPercent);
            Assert.Equal(300, window.WindowMinutes);
            Assert.Equal(DateTimeOffset.Parse("2026-07-26T16:10:00.097046+00:00"), window.ResetsAt);
        }

        [Fact]
        public void SkipsANamedBucketThatCameBackNull()
        {
            Assert.Null(ClaudeQuotaProvider.ReadNamedWindow(Parse(ObservedPayload), "Weekly", 10080, "seven_day"));
        }

        [Fact]
        public void FallsBackToTheLimitsArrayForTheWeeklyWindow()
        {
            var window = ClaudeQuotaProvider.ReadLimitsWindow(Parse(ObservedPayload), "Weekly", 10080, "weekly");

            Assert.NotNull(window);
            Assert.Equal(61d, window.UsedPercent);
            Assert.Equal(10080, window.WindowMinutes);
            Assert.Equal(DateTimeOffset.Parse("2026-07-31T01:00:00.097359+00:00"), window.ResetsAt);
        }

        [Fact]
        public void KeepsTheTightestLimitWhenAGroupReportsSeveral()
        {
            var payload = Parse(@"{ ""limits"": [
              { ""group"": ""weekly"", ""percent"": 12, ""resets_at"": ""2026-07-30T01:00:00+00:00"" },
              { ""group"": ""weekly"", ""percent"": 74, ""resets_at"": ""2026-07-31T01:00:00+00:00"" },
              { ""group"": ""weekly"", ""percent"": 55, ""resets_at"": ""2026-08-01T01:00:00+00:00"" }
            ] }");

            var window = ClaudeQuotaProvider.ReadLimitsWindow(payload, "Weekly", 10080, "weekly");

            Assert.Equal(74d, window.UsedPercent);
            Assert.Equal(DateTimeOffset.Parse("2026-07-31T01:00:00+00:00"), window.ResetsAt);
        }

        [Fact]
        public void ReturnsNothingWhenTheGroupIsAbsent()
        {
            Assert.Null(ClaudeQuotaProvider.ReadLimitsWindow(Parse(ObservedPayload), "Weekly", 10080, "monthly"));
            Assert.Null(ClaudeQuotaProvider.ReadLimitsWindow(Parse(@"{}"), "Weekly", 10080, "weekly"));
            Assert.Null(ClaudeQuotaProvider.ReadLimitsWindow(Parse(@"{ ""limits"": null }"), "Weekly", 10080, "weekly"));
        }

        [Fact]
        public void AcceptsACamelCaseResetKeyAndAUnixReset()
        {
            var camel = ClaudeQuotaProvider.ReadNamedWindow(
                Parse(@"{ ""five_hour"": { ""utilization"": 5, ""resetsAt"": ""2026-07-26T16:10:00+00:00"" } }"),
                "5h", 300, "five_hour");
            Assert.Equal(DateTimeOffset.Parse("2026-07-26T16:10:00+00:00"), camel.ResetsAt);

            var unix = ClaudeQuotaProvider.ReadNamedWindow(
                Parse(@"{ ""five_hour"": { ""utilization"": 5, ""resets_at"": 1785081000 } }"),
                "5h", 300, "five_hour");
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1785081000), unix.ResetsAt);
        }
    }
}
