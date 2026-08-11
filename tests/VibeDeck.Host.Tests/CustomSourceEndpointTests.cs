using System;
using System.Text.Json;
using VibeDeck.Host.CustomSources;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class CustomSourceEndpointTests
    {
        [Fact]
        public void PayloadDocumentOptionsRejectDeepOrCommentedJson()
        {
            Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(
                "{\"a\":{\"b\":{\"c\":{\"d\":{\"e\":{\"f\":{\"g\":{\"h\":{\"i\":{\"j\":1}}}}}}}}}}",
                CustomSourceJson.DocumentOptions));
            Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse("{/* comment */}", CustomSourceJson.DocumentOptions));
        }

        [Fact]
        public void SupportedCardTypesMatchPublicContract()
        {
            Assert.True(CustomSourceCardTypes.IsSupported("message-feed"));
            Assert.True(CustomSourceCardTypes.IsSupported("STATUS"));
            Assert.True(CustomSourceCardTypes.IsSupported("metric"));
            Assert.True(CustomSourceCardTypes.IsSupported("key-value"));
            Assert.False(CustomSourceCardTypes.IsSupported("html"));
        }

        [Fact]
        public void OptionsAreClampedToSafeOperationalBounds()
        {
            var options = new CustomSourceOptions
            {
                MaxPayloadBytes = 1,
                RequestsPerSecond = 0,
                Burst = 0,
                CleanupIntervalSeconds = 1,
                MaxSources = 0
            };

            options.Normalize();

            Assert.Equal(4096, options.MaxPayloadBytes);
            Assert.Equal(0.1, options.RequestsPerSecond);
            Assert.Equal(1, options.Burst);
            Assert.Equal(5, options.CleanupIntervalSeconds);
            Assert.Equal(1, options.MaxSources);
        }
    }
}
