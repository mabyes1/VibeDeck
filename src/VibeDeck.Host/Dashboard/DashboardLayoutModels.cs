using System.Collections.Generic;

namespace VibeDeck.Host.Dashboard
{
    public sealed class DashboardLayoutResponse
    {
        public string Profile { get; set; }
        public long Revision { get; set; }
        public string UpdatedAt { get; set; }
        public IReadOnlyList<DashboardLayoutItem> Items { get; set; }
    }

    public sealed class DashboardLayoutUpdateRequest
    {
        public string Profile { get; set; }
        public IReadOnlyList<DashboardLayoutItem> Items { get; set; }
    }

    public sealed class DashboardLayoutItem
    {
        public string Key { get; set; }
        public bool Visible { get; set; } = true;
        public int Column { get; set; }
        public int Row { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
