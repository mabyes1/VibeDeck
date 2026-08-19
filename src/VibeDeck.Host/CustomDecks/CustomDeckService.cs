using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace VibeDeck.Host.CustomDecks
{
    public sealed class CustomDeckService
    {
        private const string ExamplesSeedMarker = ".examples-seeded-v1";
        private static readonly IReadOnlyDictionary<string, string> CodingPetV1Hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["deck.json"] = "CBCC7E6C40B595F7305BAF1005A6B2D564097A7A428AC3315B5686570D08535C",
            ["index.html"] = "4C72766E49BFE25AD74BCB1F80D5E1FD98E5777B4562E433E97C9CBF50B861C2",
            ["style.css"] = "DD18F245A1AE25951151A3077D8033797AB111CAD80C8B1E2F6C8F9101108266",
            ["app.js"] = "CA63088E610DDF7EA0B044593B15DE3FE893A38029AD096325B4D61976974D31"
        };

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly string rootPath;
        private readonly string exampleRootPath;

        public CustomDeckService()
            : this(
                AppPaths.DecksDirectory,
                Path.Combine(AppContext.BaseDirectory, "DeckExamples"))
        {
        }

        internal CustomDeckService(string rootPath, string exampleRootPath = null)
        {
            this.rootPath = Path.GetFullPath(rootPath ?? throw new ArgumentNullException(nameof(rootPath)));
            this.exampleRootPath = string.IsNullOrWhiteSpace(exampleRootPath)
                ? string.Empty
                : Path.GetFullPath(exampleRootPath);
        }

        public string RootPath => rootPath;

        public void EnsureReady()
        {
            Directory.CreateDirectory(rootPath);
            SeedBundledExamplesOnce();
            RefreshBundledExampleIfPristine("coding-pet", CodingPetV1Hashes);
        }

        public CustomDeckCatalog Discover()
        {
            EnsureReady();
            var decks = new List<CustomDeckDescriptor>();
            var issues = new List<CustomDeckIssue>();

            foreach (var directory in Directory.EnumerateDirectories(rootPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var folder = Path.GetFileName(directory);
                if (!IsSafeDeckId(folder))
                {
                    issues.Add(new CustomDeckIssue
                    {
                        Folder = folder,
                        Message = "Deck folder names may contain only letters, numbers, dot, dash, and underscore."
                    });
                    continue;
                }

                var manifestPath = Path.Combine(directory, "deck.json");
                if (!File.Exists(manifestPath))
                {
                    issues.Add(new CustomDeckIssue { Folder = folder, Message = "deck.json is missing." });
                    continue;
                }

                try
                {
                    var manifest = JsonSerializer.Deserialize<CustomDeckManifest>(File.ReadAllText(manifestPath), JsonOptions);
                    decks.Add(BuildDescriptor(folder, directory, manifest));
                }
                catch (Exception ex) when (ex is JsonException || ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
                {
                    issues.Add(new CustomDeckIssue { Folder = folder, Message = ex.Message });
                }
            }

            return new CustomDeckCatalog
            {
                RootPath = rootPath,
                Decks = decks,
                Issues = issues
            };
        }

        private static CustomDeckDescriptor BuildDescriptor(string folder, string directory, CustomDeckManifest manifest)
        {
            if (manifest == null)
            {
                throw new InvalidDataException("deck.json is empty.");
            }

            var name = (manifest.Name ?? string.Empty).Trim();
            var type = string.IsNullOrWhiteSpace(manifest.Type)
                ? "static"
                : manifest.Type.Trim().ToLowerInvariant();
            if (name.Length == 0)
            {
                throw new InvalidDataException("deck.json must contain a non-empty name.");
            }

            if (type == "proxy")
            {
                var proxyTarget = CustomDeckProxyTarget.Parse(manifest.Url);
                return new CustomDeckDescriptor
                {
                    Id = folder,
                    Name = name,
                    Entry = string.Empty,
                    Icon = (manifest.Icon ?? string.Empty).Trim(),
                    Type = "proxy",
                    Url = $"/deck-proxy/{Uri.EscapeDataString(folder)}/",
                    ProxyTargetUrl = proxyTarget.ToString()
                };
            }

            if (type == "embed")
            {
                var embedTarget = ParseEmbedTarget(manifest.Url);
                return new CustomDeckDescriptor
                {
                    Id = folder,
                    Name = name,
                    Entry = string.Empty,
                    Icon = (manifest.Icon ?? string.Empty).Trim(),
                    Type = "embed",
                    Url = embedTarget.ToString()
                };
            }

            if (type != "static")
            {
                throw new InvalidDataException("deck.json type must be 'static', 'embed', or 'proxy'.");
            }

            var entry = NormalizeEntry(manifest.Entry);
            if (entry.Length == 0)
            {
                throw new InvalidDataException("deck.json must contain a non-empty entry.");
            }

            var directoryRoot = EnsureTrailingSeparator(Path.GetFullPath(directory));
            var entryPath = Path.GetFullPath(Path.Combine(directory, entry.Replace('/', Path.DirectorySeparatorChar)));
            if (!entryPath.StartsWith(directoryRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Deck entry must stay inside its Deck folder.");
            }

            if (!File.Exists(entryPath))
            {
                throw new InvalidDataException($"Deck entry '{entry}' does not exist.");
            }

            return new CustomDeckDescriptor
            {
                Id = folder,
                Name = name,
                Entry = entry,
                Icon = (manifest.Icon ?? string.Empty).Trim(),
                Type = "static",
                Url = BuildDeckUrl(folder, entry)
            };
        }

        public CustomDeckDescriptor Find(string deckId)
        {
            if (!IsSafeDeckId(deckId)) return null;
            return Discover().Decks.FirstOrDefault(deck =>
                string.Equals(deck.Id, deckId, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeEntry(string entry)
        {
            var normalized = (entry ?? string.Empty).Trim().Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(normalized))
            {
                throw new InvalidDataException("Deck entry must be a relative path.");
            }

            return normalized;
        }

        private static Uri ParseEmbedTarget(string value)
        {
            if (!Uri.TryCreate((value ?? string.Empty).Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidDataException("Embed Deck url must be an absolute HTTP or HTTPS URL.");
            }
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new InvalidDataException("Embed Deck url must not contain credentials.");
            }
            return uri;
        }

        private static string BuildDeckUrl(string folder, string entry)
        {
            var encodedEntry = string.Join("/", entry.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
            return $"/decks/{Uri.EscapeDataString(folder)}/{encodedEntry}";
        }

        private static bool IsSafeDeckId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
            {
                return false;
            }

            return value.All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.');
        }

        private static string EnsureTrailingSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private void SeedBundledExamplesOnce()
        {
            var marker = Path.Combine(rootPath, ExamplesSeedMarker);
            if (File.Exists(marker) || string.IsNullOrWhiteSpace(exampleRootPath))
            {
                return;
            }

            var source = Path.Combine(exampleRootPath, "coding-pet");
            if (!Directory.Exists(source))
            {
                return;
            }

            if (SeedExampleDeck("coding-pet"))
            {
                try
                {
                    File.WriteAllText(marker, "Custom Deck examples seeded. Delete example folders freely; VibeDeck will not recreate them.");
                }
                catch
                {
                    // Retry later if the marker could not be persisted.
                }
            }
        }

        private bool SeedExampleDeck(string deckId)
        {
            if (string.IsNullOrWhiteSpace(exampleRootPath))
            {
                return false;
            }

            var source = Path.Combine(exampleRootPath, deckId);
            var target = Path.Combine(rootPath, deckId);
            if (!Directory.Exists(source))
            {
                return false;
            }

            if (Directory.Exists(target))
            {
                return true;
            }

            try
            {
                Directory.CreateDirectory(target);
                foreach (var sourcePath in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(source, sourcePath);
                    var destination = Path.Combine(target, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(sourcePath, destination, overwrite: false);
                }
                return true;
            }
            catch
            {
                // Example seeding is optional. Discovery must still work if ProgramData is
                // temporarily read-only or another process creates the example concurrently.
                return false;
            }
        }

        private void RefreshBundledExampleIfPristine(string deckId, IReadOnlyDictionary<string, string> expectedHashes)
        {
            if (string.IsNullOrWhiteSpace(exampleRootPath) || expectedHashes == null || expectedHashes.Count == 0)
            {
                return;
            }

            var source = Path.Combine(exampleRootPath, deckId);
            var target = Path.Combine(rootPath, deckId);
            if (!Directory.Exists(source) || !Directory.Exists(target))
            {
                // A deleted bundled example is a user choice. Never resurrect it.
                return;
            }

            try
            {
                foreach (var expected in expectedHashes)
                {
                    var targetPath = Path.Combine(target, expected.Key);
                    if (!File.Exists(targetPath) || !string.Equals(GetSha256(targetPath), expected.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        // Any edit to a bundled file makes the Deck user-owned. Leave it alone.
                        return;
                    }
                }

                foreach (var sourcePath in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(source, sourcePath);
                    var destination = Path.Combine(target, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(sourcePath, destination, overwrite: true);
                }
            }
            catch
            {
                // Updating a bundled example is optional. A failed refresh must never
                // interfere with Custom Deck discovery or user-created Decks.
            }
        }

        private static string GetSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }
}
