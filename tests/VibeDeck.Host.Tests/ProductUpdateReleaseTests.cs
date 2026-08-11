using System.Text.Json;
using VibeDeck.Host;
using VibeDeck.Host.Updates;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class ProductUpdateReleaseTests
    {
        [Fact]
        public void AcceptsTrustedNewReleaseWithMatchingAssets()
        {
            using var document = JsonDocument.Parse(@"{
              ""tag_name"": ""v0.1.19"",
              ""html_url"": ""https://github.com/mabyes1/VibeDeck/releases/tag/v0.1.19"",
              ""assets"": [
                { ""name"": ""VibeDeck-Setup-0.1.19.exe"", ""browser_download_url"": ""https://github.com/mabyes1/VibeDeck/releases/download/v0.1.19/VibeDeck-Setup-0.1.19.exe"" },
                { ""name"": ""VibeDeck-Setup-0.1.19.exe.sha256"", ""browser_download_url"": ""https://github.com/mabyes1/VibeDeck/releases/download/v0.1.19/VibeDeck-Setup-0.1.19.exe.sha256"" }
              ]
            }");

            var parsed = ProductUpdateRelease.TryParse(document.RootElement, "0.1.18", out var release, out var errorCode);

            Assert.True(parsed);
            Assert.Equal("", errorCode);
            Assert.True(release.UpdateAvailable);
            Assert.Equal("0.1.19", release.Version);
            Assert.Equal("VibeDeck-Setup-0.1.19.exe", release.InstallerFileName);
        }

        [Fact]
        public void TreatsCurrentReleaseAsUpToDateWithoutAssets()
        {
            using var document = JsonDocument.Parse(@"{
              ""tag_name"": ""v0.1.18"",
              ""html_url"": ""https://github.com/mabyes1/VibeDeck/releases/tag/v0.1.18"",
              ""assets"": []
            }");

            var parsed = ProductUpdateRelease.TryParse(document.RootElement, "0.1.18", out var release, out var errorCode);

            Assert.True(parsed);
            Assert.Equal("", errorCode);
            Assert.False(release.UpdateAvailable);
        }

        [Fact]
        public void RejectsInstallerAssetsOutsideTheTrustedRepository()
        {
            using var document = JsonDocument.Parse(@"{
              ""tag_name"": ""v0.1.19"",
              ""assets"": [
                { ""name"": ""VibeDeck-Setup-0.1.19.exe"", ""browser_download_url"": ""https://example.invalid/VibeDeck-Setup-0.1.19.exe"" },
                { ""name"": ""VibeDeck-Setup-0.1.19.exe.sha256"", ""browser_download_url"": ""https://github.com/mabyes1/VibeDeck/releases/download/v0.1.19/VibeDeck-Setup-0.1.19.exe.sha256"" }
              ]
            }");

            var parsed = ProductUpdateRelease.TryParse(document.RootElement, "0.1.18", out _, out var errorCode);

            Assert.False(parsed);
            Assert.Equal("release_invalid", errorCode);
        }

        [Fact]
        public void NormalizesVersionTagsBeforeComparingThem()
        {
            Assert.True(ProductVersion.TryNormalize(" v0.1.19 ", out var normalized));
            Assert.Equal("0.1.19", normalized);
            Assert.True(ProductVersion.IsNewer("v0.1.19", "0.1.18"));
            Assert.False(ProductVersion.IsNewer("0.1.18", "0.1.18"));
        }
    }
}
