using System;
using System.IO;
using System.Text.Json;

namespace VibeDeck.Host.Appearance
{
    public sealed class AppearanceThemeService
    {
        private const int MaxBackgroundBytes = 3 * 1024 * 1024;
        private readonly object gate = new object();
        private readonly string directory;
        private readonly string themePath;
        private readonly string backgroundPath;
        private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public AppearanceThemeService()
            : this(null)
        {
        }

        public AppearanceThemeService(string directoryOverride)
        {
            directory = string.IsNullOrWhiteSpace(directoryOverride)
                ? AppPaths.AppearanceDirectory
                : directoryOverride;
            themePath = Path.Combine(directory, "theme.json");
            backgroundPath = Path.Combine(directory, "background.webp");
        }

        public AppearanceThemeResponse Get()
        {
            lock (gate)
            {
                var configured = File.Exists(themePath);
                var settings = configured ? ReadTheme() : new AppearanceThemeSettings();
                return BuildResponse(settings, configured);
            }
        }

        public AppearanceThemeResponse Save(AppearanceThemeSettings request)
        {
            if (request == null) throw new AppearanceThemeException("布景設定不能是空的。");
            lock (gate)
            {
                var normalized = Normalize(request);
                if (normalized.BackgroundMode == "image" && !File.Exists(backgroundPath))
                {
                    normalized.BackgroundMode = "gradient";
                }
                PersistTheme(normalized);
                return BuildResponse(normalized, true);
            }
        }

        public AppearanceThemeResponse SaveBackground(AppearanceBackgroundRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.DataUrl))
            {
                throw new AppearanceThemeException("背景圖片不能是空的。");
            }

            const string prefix = "data:image/webp;base64,";
            var value = request.DataUrl.Trim();
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new AppearanceThemeException("背景圖片必須是 WebP 格式。");
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(value.Substring(prefix.Length));
            }
            catch (FormatException)
            {
                throw new AppearanceThemeException("背景圖片資料格式無效。");
            }

            if (bytes.Length == 0 || bytes.Length > MaxBackgroundBytes)
            {
                throw new AppearanceThemeException("背景圖片必須小於 3 MB。");
            }

            if (!LooksLikeWebP(bytes))
            {
                throw new AppearanceThemeException("背景圖片不是有效的 WebP 檔案。");
            }

            lock (gate)
            {
                Directory.CreateDirectory(directory);
                WriteAtomicBytes(backgroundPath, bytes);
                var settings = File.Exists(themePath) ? ReadTheme() : new AppearanceThemeSettings();
                settings.BackgroundMode = "image";
                PersistTheme(settings);
                return BuildResponse(settings, true);
            }
        }

        public AppearanceThemeResponse ClearBackground()
        {
            lock (gate)
            {
                try
                {
                    if (File.Exists(backgroundPath)) File.Delete(backgroundPath);
                }
                catch (IOException error)
                {
                    throw new AppearanceThemeException($"背景圖片無法刪除：{error.Message}");
                }
                catch (UnauthorizedAccessException error)
                {
                    throw new AppearanceThemeException($"背景圖片無法刪除：{error.Message}");
                }

                var settings = File.Exists(themePath) ? ReadTheme() : new AppearanceThemeSettings();
                if (settings.BackgroundMode == "image") settings.BackgroundMode = "gradient";
                PersistTheme(settings);
                return BuildResponse(settings, true);
            }
        }

        public bool TryReadBackground(out byte[] bytes, out long version)
        {
            lock (gate)
            {
                if (!File.Exists(backgroundPath))
                {
                    bytes = null;
                    version = 0;
                    return false;
                }

                bytes = File.ReadAllBytes(backgroundPath);
                version = File.GetLastWriteTimeUtc(backgroundPath).Ticks;
                return true;
            }
        }

        private AppearanceThemeSettings ReadTheme()
        {
            try
            {
                var text = File.ReadAllText(themePath);
                return Normalize(JsonSerializer.Deserialize<AppearanceThemeSettings>(text, jsonOptions));
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is JsonException)
            {
                throw new AppearanceThemeException($"布景設定無法讀取：{error.Message}");
            }
        }

        private void PersistTheme(AppearanceThemeSettings settings)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var tempPath = themePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(tempPath, JsonSerializer.Serialize(Normalize(settings), jsonOptions));
                    File.Move(tempPath, themePath, true);
                }
                finally
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                throw new AppearanceThemeException($"布景設定無法寫入：{error.Message}");
            }
        }

        private AppearanceThemeResponse BuildResponse(AppearanceThemeSettings settings, bool configured)
        {
            var hasBackground = File.Exists(backgroundPath);
            var version = hasBackground ? File.GetLastWriteTimeUtc(backgroundPath).Ticks : 0;
            return new AppearanceThemeResponse
            {
                Configured = configured,
                Theme = Normalize(settings),
                HasBackgroundImage = hasBackground,
                BackgroundUrl = hasBackground ? $"/api/appearance/background?v={version}" : string.Empty
            };
        }

        private static AppearanceThemeSettings Normalize(AppearanceThemeSettings value)
        {
            value ??= new AppearanceThemeSettings();
            return new AppearanceThemeSettings
            {
                BackgroundMode = NormalizeChoice(value.BackgroundMode, "gradient", "solid", "gradient", "image"),
                ColorA = NormalizeColor(value.ColorA, "#4b1f66"),
                ColorB = NormalizeColor(value.ColorB, "#17344d"),
                Angle = Clamp(value.Angle, 0, 360, 132),
                Intensity = Clamp(value.Intensity, 30, 100, 72),
                BackgroundFit = NormalizeChoice(value.BackgroundFit, "cover", "cover", "contain"),
                BackgroundPosition = NormalizeChoice(value.BackgroundPosition, "center", "center", "top", "bottom"),
                BackgroundBlur = Clamp(value.BackgroundBlur, 0, 30, 0),
                BackgroundDim = Clamp(value.BackgroundDim, 0, 70, 24),
                GlassOpacity = Clamp(value.GlassOpacity, 2, 20, 7),
                GlassBlur = Clamp(value.GlassBlur, 0, 40, 20),
                GlassBorder = Clamp(value.GlassBorder, 4, 30, 12)
            };
        }

        private static string NormalizeChoice(string value, string fallback, params string[] choices)
        {
            var candidate = (value ?? string.Empty).Trim().ToLowerInvariant();
            foreach (var choice in choices)
            {
                if (candidate == choice) return choice;
            }
            return fallback;
        }

        private static string NormalizeColor(string value, string fallback)
        {
            var candidate = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (candidate.Length == 7 && candidate[0] == '#')
            {
                for (var i = 1; i < candidate.Length; i++)
                {
                    if (!Uri.IsHexDigit(candidate[i])) return fallback;
                }
                return candidate;
            }
            return fallback;
        }

        private static int Clamp(int value, int min, int max, int fallback)
        {
            if (value < min || value > max) return fallback;
            return value;
        }

        private static bool LooksLikeWebP(byte[] bytes)
        {
            return bytes.Length >= 12 &&
                bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
                bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P';
        }

        private static void WriteAtomicBytes(string path, byte[] bytes)
        {
            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(tempPath, bytes);
                File.Move(tempPath, path, true);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
    }

    public sealed class AppearanceThemeSettings
    {
        public string BackgroundMode { get; set; } = "gradient";
        public string ColorA { get; set; } = "#4b1f66";
        public string ColorB { get; set; } = "#17344d";
        public int Angle { get; set; } = 132;
        public int Intensity { get; set; } = 72;
        public string BackgroundFit { get; set; } = "cover";
        public string BackgroundPosition { get; set; } = "center";
        public int BackgroundBlur { get; set; } = 0;
        public int BackgroundDim { get; set; } = 24;
        public int GlassOpacity { get; set; } = 7;
        public int GlassBlur { get; set; } = 20;
        public int GlassBorder { get; set; } = 12;
    }

    public sealed class AppearanceThemeResponse
    {
        public bool Configured { get; set; }
        public AppearanceThemeSettings Theme { get; set; }
        public bool HasBackgroundImage { get; set; }
        public string BackgroundUrl { get; set; }
    }

    public sealed class AppearanceBackgroundRequest
    {
        public string DataUrl { get; set; }
    }

    public sealed class AppearanceThemeException : Exception
    {
        public AppearanceThemeException(string message) : base(message) { }
    }
}
