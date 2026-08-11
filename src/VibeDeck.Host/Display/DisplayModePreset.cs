namespace VibeDeck.Host.Display
{
    public sealed class DisplayModePreset
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int RefreshRate { get; set; }
        public string Kind { get; set; }
    }
}
