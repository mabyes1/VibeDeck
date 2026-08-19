using System.Collections.Generic;
using VibeDeck.Host.Windows;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class DisplayCatalogTests
    {
        [Fact]
        public void DxgiOutputMapWinsOverWin32MonitorEnumerationOrder()
        {
            var displays = new List<DisplayInfo>
            {
                new DisplayInfo { DeviceName = @"\\.\DISPLAY5", OutputIndex = 0 },
                new DisplayInfo { DeviceName = @"\\.\DISPLAY1", OutputIndex = 1 },
                new DisplayInfo { DeviceName = @"\\.\DISPLAY2", OutputIndex = 2 }
            };
            var dxgi = new Dictionary<string, DxgiOutputLocation>
            {
                [@"\\.\DISPLAY1"] = new DxgiOutputLocation(0, 0),
                [@"\\.\DISPLAY2"] = new DxgiOutputLocation(0, 1),
                [@"\\.\DISPLAY5"] = new DxgiOutputLocation(0, 2)
            };

            DisplayCatalog.ApplyDxgiOutputMap(displays, dxgi);

            Assert.Equal(2, displays[0].OutputIndex);
            Assert.Equal(0, displays[1].OutputIndex);
            Assert.Equal(1, displays[2].OutputIndex);
            Assert.All(displays, display => Assert.Equal(0, display.AdapterIndex));
        }

        [Fact]
        public void UnknownDxgiOutputFailsClosedInsteadOfCapturingOutputZero()
        {
            var displays = new List<DisplayInfo>
            {
                new DisplayInfo { DeviceName = @"\\.\DISPLAY9", AdapterIndex = 0, OutputIndex = 0 }
            };

            DisplayCatalog.ApplyDxgiOutputMap(displays, new Dictionary<string, DxgiOutputLocation>());

            Assert.Equal(-1, displays[0].AdapterIndex);
            Assert.Equal(-1, displays[0].OutputIndex);
        }
    }
}
