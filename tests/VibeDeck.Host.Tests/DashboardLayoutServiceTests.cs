using System;
using System.IO;
using System.Linq;
using VibeDeck.Host.Dashboard;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class DashboardLayoutServiceTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "VibeDeckLayoutTests", Guid.NewGuid().ToString("N"));

        [Fact]
        public void EinkLandscapeDefaultFitsOneSixRowCanvas()
        {
            var service = CreateService();
            var layout = service.Get("eink-landscape");

            Assert.Equal("eink-landscape", layout.Profile);
            Assert.Contains(layout.Items, item => item.Key == "activity-feed" && item.Visible && item.Width == 8 && item.Height == 2);
            Assert.Contains(layout.Items, item => item.Key == "quota-mini" && item.Visible && item.Height == 2);
            Assert.All(layout.Items.Where(item => new[] { "cpu", "ram", "gpu", "vram", "disk", "network" }.Contains(item.Key)), item => Assert.Equal(1, item.Height));
            Assert.All(layout.Items.Where(item => item.Visible), item => Assert.True(item.Row + item.Height <= 6));
        }

        [Theory]
        [InlineData("tablet-landscape")]
        [InlineData("tablet-portrait")]
        public void LegacyTabletProfilesNormalizeToDefault(string profile)
        {
            var service = CreateService();
            var layout = service.Get(profile);

            Assert.Equal("default", layout.Profile);
            Assert.Contains(layout.Items, item => item.Key == "activity-feed" && item.Visible);
            Assert.Contains(layout.Items, item => item.Key == "quota-mini" && item.Visible);
            Assert.All(layout.Items.Where(item => item.Visible), item => Assert.True(item.Row + item.Height <= 6));
        }

        [Fact]
        public void SavedLayoutPersistsAcrossServiceInstances()
        {
            var path = Path.Combine(directory, "layouts.json");
            Directory.CreateDirectory(directory);
            var service = new DashboardLayoutService(path);
            var initial = service.Get("eink-landscape");
            var moved = initial.Items.Select(item => new DashboardLayoutItem
            {
                Key = item.Key,
                Visible = item.Key != "activity-feed",
                Column = item.Key == "system-load" ? 2 : item.Column,
                Row = item.Row,
                Width = item.Width,
                Height = item.Height
            }).ToArray();

            service.Save(new DashboardLayoutUpdateRequest { Profile = "eink-landscape", Items = moved });
            var reloaded = new DashboardLayoutService(path).Get("eink-landscape");

            Assert.Equal(1, reloaded.Revision);
            Assert.Equal(2, reloaded.Items.Single(item => item.Key == "system-load").Column);
            Assert.False(reloaded.Items.Single(item => item.Key == "activity-feed").Visible);
        }

        [Fact]
        public void InvalidCardGeometryIsRejected()
        {
            var service = CreateService();
            Assert.Throws<DashboardLayoutException>(() => service.Save(new DashboardLayoutUpdateRequest
            {
                Profile = "default",
                Items = new[]
                {
                    new DashboardLayoutItem { Key = "cpu", Visible = true, Column = 11, Row = 0, Width = 2, Height = 2 }
                }
            }));
        }

        [Fact]
        public void AllHiddenLayoutIsRejected()
        {
            var service = CreateService();
            Assert.Throws<DashboardLayoutException>(() => service.Save(new DashboardLayoutUpdateRequest
            {
                Profile = "default",
                Items = new[]
                {
                    new DashboardLayoutItem { Key = "cpu", Visible = false, Column = 0, Row = 0, Width = 4, Height = 1 },
                    new DashboardLayoutItem { Key = "ram", Visible = false, Column = 4, Row = 0, Width = 4, Height = 1 }
                }
            }));
        }

        [Fact]
        public void LegacyTabletProfileIsIgnoredInFavorOfDefault()
        {
            var path = Path.Combine(directory, "layouts.json");
            Directory.CreateDirectory(directory);
            File.WriteAllText(path,
                "{\"profiles\":{\"tablet-portrait\":{\"profile\":\"tablet-portrait\",\"revision\":7,\"updatedAt\":\"2026-08-08T00:00:00Z\",\"items\":[" +
                "{\"key\":\"cpu\",\"visible\":false,\"column\":0,\"row\":0,\"width\":4,\"height\":1}," +
                "{\"key\":\"ram\",\"visible\":false,\"column\":4,\"row\":0,\"width\":4,\"height\":1}]}}}");

            var layout = new DashboardLayoutService(path).Get("tablet-portrait");

            Assert.Equal("default", layout.Profile);
            Assert.Equal(0, layout.Revision);
            Assert.Contains(layout.Items, item => item.Key == "activity-feed" && item.Visible);
            Assert.All(layout.Items, item => Assert.True(item.Visible));
        }

        [Fact]
        public void CorruptPrimaryLayoutRecoversThePreviousSavedLayout()
        {
            var path = Path.Combine(directory, "layouts.json");
            Directory.CreateDirectory(directory);
            var service = new DashboardLayoutService(path);
            var initial = service.Get("default");
            var first = initial.Items.Select(item => new DashboardLayoutItem
            {
                Key = item.Key,
                Visible = item.Key != "activity-feed",
                Column = item.Column,
                Row = item.Row,
                Width = item.Width,
                Height = item.Height
            }).ToArray();
            service.Save(new DashboardLayoutUpdateRequest { Profile = "default", Items = first });

            var second = first.Select(item => new DashboardLayoutItem
            {
                Key = item.Key,
                Visible = item.Visible,
                Column = item.Key == "system-load" ? 2 : item.Column,
                Row = item.Row,
                Width = item.Width,
                Height = item.Height
            }).ToArray();
            service.Save(new DashboardLayoutUpdateRequest { Profile = "default", Items = second });

            File.WriteAllText(path, "{ broken json");
            var recovered = new DashboardLayoutService(path).Get("default");

            Assert.Equal(1, recovered.Revision);
            Assert.False(recovered.Items.Single(item => item.Key == "activity-feed").Visible);
            Assert.Equal(0, recovered.Items.Single(item => item.Key == "system-load").Column);
            Assert.NotEmpty(Directory.GetFiles(directory, "layouts.json.corrupt-*.json"));
        }

        private DashboardLayoutService CreateService()
        {
            Directory.CreateDirectory(directory);
            return new DashboardLayoutService(Path.Combine(directory, "layouts.json"));
        }

        public void Dispose()
        {
            try { Directory.Delete(directory, true); } catch { }
        }
    }
}
