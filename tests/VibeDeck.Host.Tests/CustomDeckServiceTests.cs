using System;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Http;
using VibeDeck.Host.CustomDecks;
using VibeDeck.Host.Security;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class CustomDeckServiceTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "VibeDeckCustomDeckTests", Guid.NewGuid().ToString("N"));

        [Fact]
        public void EmptyDeckDirectoryProducesEmptyCatalog()
        {
            var service = new CustomDeckService(Path.Combine(root, "Decks"));

            var catalog = service.Discover();

            Assert.Empty(catalog.Decks);
            Assert.Empty(catalog.Issues);
            Assert.True(Directory.Exists(service.RootPath));
        }

        [Fact]
        public void ValidDeckIsDiscoveredWithWebUrl()
        {
            WriteDeck("coding-pet", "{\"name\":\"Coding Pet\",\"entry\":\"index.html\",\"icon\":\"🐱\"}", createEntry: true);
            var service = new CustomDeckService(Path.Combine(root, "Decks"));

            var deck = Assert.Single(service.Discover().Decks);

            Assert.Equal("coding-pet", deck.Id);
            Assert.Equal("Coding Pet", deck.Name);
            Assert.Equal("🐱", deck.Icon);
            Assert.Equal("/decks/coding-pet/index.html", deck.Url);
            Assert.Equal("static", deck.Type);
        }

        [Fact]
        public void PrivateNetworkProxyDeckIsDiscovered()
        {
            WriteDeck("internal-monitor", "{\"name\":\"Internal Monitor\",\"type\":\"proxy\",\"url\":\"http://10.0.0.42:8666\"}", createEntry: false);

            var deck = Assert.Single(new CustomDeckService(Path.Combine(root, "Decks")).Discover().Decks);

            Assert.Equal("proxy", deck.Type);
            Assert.Equal("/deck-proxy/internal-monitor/", deck.Url);
            Assert.Equal("http://10.0.0.42:8666/", deck.ProxyTargetUrl);
        }

        [Fact]
        public void EmbedDeckUsesItsRemoteUrlDirectly()
        {
            WriteDeck("internal-monitor", "{\"name\":\"Internal Monitor\",\"type\":\"embed\",\"url\":\"http://10.0.0.42:8666/\"}", createEntry: false);

            var deck = Assert.Single(new CustomDeckService(Path.Combine(root, "Decks")).Discover().Decks);

            Assert.Equal("embed", deck.Type);
            Assert.Equal("http://10.0.0.42:8666/", deck.Url);
            Assert.Empty(deck.Entry);
            Assert.Empty(deck.ProxyTargetUrl);
        }

        [Theory]
        [InlineData("javascript:alert(1)")]
        [InlineData("file:///C:/Windows/win.ini")]
        [InlineData("http://user:password@10.0.0.42:8666/")]
        public void UnsafeEmbedTargetBecomesIssue(string url)
        {
            WriteDeck("unsafe-embed", $"{{\"name\":\"Unsafe\",\"type\":\"embed\",\"url\":\"{url}\"}}", createEntry: false);

            var catalog = new CustomDeckService(Path.Combine(root, "Decks")).Discover();

            Assert.Empty(catalog.Decks);
            Assert.Single(catalog.Issues);
        }

        [Theory]
        [InlineData("https://8.8.8.8/")]
        [InlineData("https://example.com/")]
        [InlineData("file:///C:/Windows/win.ini")]
        public void UnsafeProxyTargetBecomesIssue(string url)
        {
            WriteDeck("unsafe", $"{{\"name\":\"Unsafe\",\"type\":\"proxy\",\"url\":\"{url}\"}}", createEntry: false);

            var catalog = new CustomDeckService(Path.Combine(root, "Decks")).Discover();

            Assert.Empty(catalog.Decks);
            Assert.Single(catalog.Issues);
        }

        [Fact]
        public void ProxyHtmlIsRewrittenUnderDeckRoute()
        {
            var deck = new CustomDeckDescriptor { Id = "internal-monitor", Type = "proxy" };
            var upstream = new Uri("http://10.0.0.42:8666/");

            var html = CustomDeckProxyService.RewriteText(
                "<html><head></head><body><form action=\"/login\"><script src=\"/app.js\"></script></form></body></html>",
                "text/html",
                deck,
                upstream);

            Assert.Contains("<base href=\"/deck-proxy/internal-monitor/\">", html);
            Assert.Contains("action=\"/deck-proxy/internal-monitor/login\"", html);
            Assert.Contains("src=\"/deck-proxy/internal-monitor/app.js\"", html);
            Assert.Contains("const prefix = \"/deck-proxy/internal-monitor/\"", html);
        }

        [Fact]
        public void ProxyRequestCannotEscapeConfiguredOrigin()
        {
            var upstream = new Uri("http://10.0.0.42:8666/");

            var target = CustomDeckProxyService.BuildTarget(upstream, "status", "?full=1");

            Assert.Equal("http://10.0.0.42:8666/status?full=1", target.ToString());
        }

        [Fact]
        public void ProxySessionIdentitySurvivesSandboxedIframeRequestsWithoutProxyCookie()
        {
            var first = new DefaultHttpContext();
            first.Connection.RemoteIpAddress = IPAddress.Parse("192.168.0.55");
            first.Request.Headers.UserAgent = "Tablet Browser";
            var second = new DefaultHttpContext();
            second.Connection.RemoteIpAddress = IPAddress.Parse("192.168.0.55");
            second.Request.Headers.UserAgent = "Tablet Browser";

            Assert.Equal(
                CustomDeckProxyService.BuildSessionIdentity(first),
                CustomDeckProxyService.BuildSessionIdentity(second));
        }

        [Fact]
        public void ProxySessionIdentityPrefersPairedDeviceToken()
        {
            var first = new DefaultHttpContext();
            first.Connection.RemoteIpAddress = IPAddress.Parse("192.168.0.55");
            first.Request.Headers.Cookie = $"{DeviceTrustService.CookieName}=paired-device-token";
            var second = new DefaultHttpContext();
            second.Connection.RemoteIpAddress = IPAddress.Parse("192.168.0.99");
            second.Request.Headers[DeviceTrustService.HeaderName] = "paired-device-token";

            Assert.Equal(
                CustomDeckProxyService.BuildSessionIdentity(first),
                CustomDeckProxyService.BuildSessionIdentity(second));
        }

        [Fact]
        public void MultipleDecksCanCoexist()
        {
            WriteDeck("alpha", "{\"name\":\"Alpha\",\"entry\":\"index.html\"}", createEntry: true);
            WriteDeck("beta", "{\"name\":\"Beta\",\"entry\":\"index.html\"}", createEntry: true);

            var catalog = new CustomDeckService(Path.Combine(root, "Decks")).Discover();

            Assert.Equal(new[] { "alpha", "beta" }, catalog.Decks.Select(deck => deck.Id).ToArray());
        }

        [Fact]
        public void MalformedManifestBecomesIssueWithoutBreakingOtherDecks()
        {
            WriteDeck("good", "{\"name\":\"Good\",\"entry\":\"index.html\"}", createEntry: true);
            WriteDeck("broken", "{ definitely not json", createEntry: true);

            var catalog = new CustomDeckService(Path.Combine(root, "Decks")).Discover();

            Assert.Single(catalog.Decks);
            Assert.Equal("good", catalog.Decks[0].Id);
            Assert.Contains(catalog.Issues, issue => issue.Folder == "broken");
        }

        [Fact]
        public void MissingEntryBecomesActionableIssue()
        {
            WriteDeck("missing", "{\"name\":\"Missing\",\"entry\":\"index.html\"}", createEntry: false);

            var catalog = new CustomDeckService(Path.Combine(root, "Decks")).Discover();

            Assert.Empty(catalog.Decks);
            var issue = Assert.Single(catalog.Issues);
            Assert.Equal("missing", issue.Folder);
            Assert.Contains("does not exist", issue.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EntryCannotEscapeDeckFolder()
        {
            Directory.CreateDirectory(Path.Combine(root, "Decks"));
            File.WriteAllText(Path.Combine(root, "outside.html"), "outside");
            WriteDeck("escape", "{\"name\":\"Escape\",\"entry\":\"../../outside.html\"}", createEntry: false);

            var catalog = new CustomDeckService(Path.Combine(root, "Decks")).Discover();

            Assert.Empty(catalog.Decks);
            Assert.Contains(catalog.Issues, issue => issue.Folder == "escape" && issue.Message.Contains("inside", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ExampleIsSeededOnceAndNeverOverwritesUserCopy()
        {
            var exampleRoot = Path.Combine(root, "Examples");
            var exampleDeck = Path.Combine(exampleRoot, "coding-pet");
            Directory.CreateDirectory(exampleDeck);
            File.WriteAllText(Path.Combine(exampleDeck, "deck.json"), "{\"name\":\"Coding Pet\",\"entry\":\"index.html\"}");
            File.WriteAllText(Path.Combine(exampleDeck, "index.html"), "original");

            var service = new CustomDeckService(Path.Combine(root, "Decks"), exampleRoot);
            service.EnsureReady();
            var installedEntry = Path.Combine(service.RootPath, "coding-pet", "index.html");
            Assert.Equal("original", File.ReadAllText(installedEntry));

            File.WriteAllText(installedEntry, "user edited");
            service.EnsureReady();

            Assert.Equal("user edited", File.ReadAllText(installedEntry));
        }

        [Fact]
        public void DeletedExampleDoesNotRespawnAfterSeedMarkerExists()
        {
            var exampleRoot = Path.Combine(root, "Examples");
            var exampleDeck = Path.Combine(exampleRoot, "coding-pet");
            Directory.CreateDirectory(exampleDeck);
            File.WriteAllText(Path.Combine(exampleDeck, "deck.json"), "{\"name\":\"Coding Pet\",\"entry\":\"index.html\"}");
            File.WriteAllText(Path.Combine(exampleDeck, "index.html"), "original");

            var service = new CustomDeckService(Path.Combine(root, "Decks"), exampleRoot);
            service.EnsureReady();
            Directory.Delete(Path.Combine(service.RootPath, "coding-pet"), recursive: true);

            service.EnsureReady();

            Assert.False(Directory.Exists(Path.Combine(service.RootPath, "coding-pet")));
            Assert.Empty(service.Discover().Decks);
        }

        private void WriteDeck(string id, string manifest, bool createEntry)
        {
            var directory = Path.Combine(root, "Decks", id);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "deck.json"), manifest);
            if (createEntry)
            {
                File.WriteAllText(Path.Combine(directory, "index.html"), "<!doctype html><title>Deck</title>");
            }
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
