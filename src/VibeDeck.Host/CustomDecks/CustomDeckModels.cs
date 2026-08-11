using System.Collections.Generic;

namespace VibeDeck.Host.CustomDecks
{
    public sealed class CustomDeckManifest
    {
        public string Name { get; set; } = string.Empty;

        public string Entry { get; set; } = string.Empty;

        public string Icon { get; set; } = string.Empty;
    }

    public sealed class CustomDeckDescriptor
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Entry { get; set; } = string.Empty;

        public string Icon { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;
    }

    public sealed class CustomDeckIssue
    {
        public string Folder { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;
    }

    public sealed class CustomDeckCatalog
    {
        public string RootPath { get; set; } = string.Empty;

        public IReadOnlyList<CustomDeckDescriptor> Decks { get; set; } = new List<CustomDeckDescriptor>();

        public IReadOnlyList<CustomDeckIssue> Issues { get; set; } = new List<CustomDeckIssue>();
    }
}
