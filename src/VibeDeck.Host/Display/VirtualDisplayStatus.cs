namespace VibeDeck.Host.Display
{
    public sealed class VirtualDisplayStatus
    {
        public bool DriverInstalled { get; set; }
        public bool DisplayEnabled { get; set; }
        public string DevicePath { get; set; }
        public string State { get; set; }
        public string Detail { get; set; }
    }
}
