using System;
using System.IO;
using VibeDeck.Host.Appearance;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class AppearanceThemeServiceTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "VibeDeck-AppearanceTests-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void Theme_and_background_persist_across_service_restart()
        {
            var service = new AppearanceThemeService(root);
            Assert.False(service.Get().Configured);

            var saved = service.Save(new AppearanceThemeSettings
            {
                BackgroundMode = "gradient",
                ColorA = "#112233",
                ColorB = "#445566",
                Angle = 210,
                Intensity = 81,
                BackgroundDim = 12,
                GlassOpacity = 9,
                GlassBlur = 24,
                GlassBorder = 14
            });

            Assert.True(saved.Configured);
            Assert.Equal("#112233", saved.Theme.ColorA);

            var background = service.SaveBackground(new AppearanceBackgroundRequest
            {
                DataUrl = "data:image/webp;base64," + Convert.ToBase64String(FakeWebP())
            });

            Assert.True(background.HasBackgroundImage);
            Assert.Equal("image", background.Theme.BackgroundMode);
            Assert.StartsWith("/api/appearance/background?v=", background.BackgroundUrl);

            var restarted = new AppearanceThemeService(root).Get();
            Assert.True(restarted.Configured);
            Assert.True(restarted.HasBackgroundImage);
            Assert.Equal("#112233", restarted.Theme.ColorA);
            Assert.Equal("image", restarted.Theme.BackgroundMode);
        }

        [Fact]
        public void Clearing_background_preserves_theme_and_returns_to_gradient()
        {
            var service = new AppearanceThemeService(root);
            service.Save(new AppearanceThemeSettings { ColorA = "#123456" });
            service.SaveBackground(new AppearanceBackgroundRequest
            {
                DataUrl = "data:image/webp;base64," + Convert.ToBase64String(FakeWebP())
            });

            var result = service.ClearBackground();

            Assert.False(result.HasBackgroundImage);
            Assert.Equal("gradient", result.Theme.BackgroundMode);
            Assert.Equal("#123456", result.Theme.ColorA);
        }

        [Fact]
        public void Invalid_background_payload_is_rejected()
        {
            var service = new AppearanceThemeService(root);
            Assert.Throws<AppearanceThemeException>(() => service.SaveBackground(new AppearanceBackgroundRequest
            {
                DataUrl = "data:image/png;base64,AAAA"
            }));
        }

        private static byte[] FakeWebP()
        {
            return new byte[]
            {
                (byte)'R', (byte)'I', (byte)'F', (byte)'F',
                4, 0, 0, 0,
                (byte)'W', (byte)'E', (byte)'B', (byte)'P',
                0, 0, 0, 0
            };
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
