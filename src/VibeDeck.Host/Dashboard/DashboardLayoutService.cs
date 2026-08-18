using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VibeDeck.Host.Diagnostics;

namespace VibeDeck.Host.Dashboard
{
    public sealed class DashboardLayoutService
    {
        private const int ColumnCount = 12;
        private readonly object gate = new object();
        private readonly string storePath;
        private readonly string backupPath;
        private readonly AuditTrailService audit;
        private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        private DashboardLayoutStore store;

        public DashboardLayoutService()
            : this(null, null)
        {
        }

        public DashboardLayoutService(AuditTrailService audit)
            : this(null, audit)
        {
        }

        public DashboardLayoutService(string storePathOverride)
            : this(storePathOverride, null)
        {
        }

        public DashboardLayoutService(string storePathOverride, AuditTrailService audit)
        {
            var directory = AppPaths.EnsureDirectory(AppPaths.DashboardDirectory);
            storePath = string.IsNullOrWhiteSpace(storePathOverride)
                ? Path.Combine(directory, "layouts.json")
                : storePathOverride;
            backupPath = storePath + ".bak";
            this.audit = audit;
            store = Load();
        }

        public DashboardLayoutResponse Get(string profile)
        {
            var normalized = NormalizeProfile(profile);
            lock (gate)
            {
                if (!store.Profiles.TryGetValue(normalized, out var saved))
                {
                    return CreateDefault(normalized);
                }

                // A dashboard with no visible cards is not a usable information
                // board and older clients could persist this state. Return the
                // default as revision 0 so the browser heals it through the
                // existing automatic-save path instead of trapping the user on
                // an empty wallpaper.
                if (saved.Items == null || saved.Items.Count == 0 || !saved.Items.Any(item => item?.Visible == true))
                {
                    return CreateDefault(normalized);
                }

                return Clone(saved);
            }
        }

        public DashboardLayoutResponse Save(DashboardLayoutUpdateRequest request)
        {
            if (request == null) throw new DashboardLayoutException("版面資料不能是空的。");
            var profile = NormalizeProfile(request.Profile);
            var items = ValidateAndClone(request.Items);

            lock (gate)
            {
                var hadPrevious = store.Profiles.TryGetValue(profile, out var previous);
                var revision = hadPrevious
                    ? previous.Revision + 1
                    : 1;
                var response = new DashboardLayoutResponse
                {
                    Profile = profile,
                    Revision = revision,
                    UpdatedAt = DateTimeOffset.UtcNow.ToString("O"),
                    Items = items
                };
                store.Profiles[profile] = response;
                try
                {
                    Persist();
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    RestoreProfile(profile, hadPrevious, previous);
                    audit?.RecordException("dashboard-layout", "save", error, subject: profile);
                    throw new DashboardLayoutException("版面無法寫入磁碟，已保留上一版。請確認 VibeDeck 資料夾權限後再試。");
                }
                return Clone(response);
            }
        }

        public DashboardLayoutResponse Reset(string profile)
        {
            var normalized = NormalizeProfile(profile);
            lock (gate)
            {
                var response = CreateDefault(normalized);
                var hadPrevious = store.Profiles.TryGetValue(normalized, out var previous);
                response.Revision = hadPrevious
                    ? previous.Revision + 1
                    : 1;
                response.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
                store.Profiles[normalized] = response;
                try
                {
                    Persist();
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    RestoreProfile(normalized, hadPrevious, previous);
                    audit?.RecordException("dashboard-layout", "reset", error, subject: normalized);
                    throw new DashboardLayoutException("預設版面無法寫入磁碟，已保留上一版。請確認 VibeDeck 資料夾權限後再試。");
                }
                return Clone(response);
            }
        }

        public static string NormalizeProfile(string profile)
        {
            var normalized = (profile ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "eink-landscape" => normalized,
                "eink-portrait" => normalized,
                "default" => normalized,
                _ => "default"
            };
        }

        private DashboardLayoutStore Load()
        {
            var primary = ReadStore(storePath, out var primaryError);
            if (primary != null)
            {
                return primary;
            }

            var backup = ReadStore(backupPath, out var backupError);
            if (backup != null)
            {
                TryRestoreBackup();
                audit?.Record(
                    "warning",
                    "dashboard-layout",
                    "load",
                    "recovered-from-backup",
                    details: new Dictionary<string, string>
                    {
                        ["primaryError"] = primaryError ?? "primary-missing"
                    });
                return backup;
            }

            if (!string.IsNullOrWhiteSpace(primaryError))
            {
                audit?.Record(
                    "error",
                    "dashboard-layout",
                    "load",
                    "unreadable",
                    details: new Dictionary<string, string>
                    {
                        ["primaryError"] = primaryError,
                        ["backupError"] = backupError ?? "backup-missing"
                    });
            }

            return new DashboardLayoutStore();
        }

        private void Persist()
        {
            var directory = Path.GetDirectoryName(storePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var tempPath = storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempPath, JsonSerializer.Serialize(store, jsonOptions));
                if (File.Exists(storePath))
                {
                    File.Copy(storePath, backupPath, true);
                    File.Move(tempPath, storePath, true);
                }
                else
                {
                    File.Move(tempPath, storePath);
                    File.Copy(storePath, backupPath, true);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private DashboardLayoutStore ReadStore(string path, out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(path)) return null;
                var text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text))
                {
                    error = "layout file is empty";
                    return null;
                }

                var loaded = JsonSerializer.Deserialize<DashboardLayoutStore>(text, jsonOptions);
                if (loaded == null)
                {
                    error = "layout file contains no data";
                    return null;
                }

                loaded.Profiles ??= new Dictionary<string, DashboardLayoutResponse>(StringComparer.OrdinalIgnoreCase);
                return loaded;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                error = exception.Message;
                return null;
            }
        }

        private void TryRestoreBackup()
        {
            try
            {
                if (File.Exists(storePath))
                {
                    var corruptPath = storePath + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json";
                    File.Copy(storePath, corruptPath, false);
                }
                File.Copy(backupPath, storePath, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void RestoreProfile(string profile, bool hadPrevious, DashboardLayoutResponse previous)
        {
            if (hadPrevious)
            {
                store.Profiles[profile] = previous;
            }
            else
            {
                store.Profiles.Remove(profile);
            }
        }

        private static IReadOnlyList<DashboardLayoutItem> ValidateAndClone(IReadOnlyList<DashboardLayoutItem> items)
        {
            if (items == null) throw new DashboardLayoutException("版面缺少卡片資料。");
            if (items.Count > 80) throw new DashboardLayoutException("單一版面最多 80 張卡片。");
            if (items.Count == 0 || !items.Any(item => item?.Visible == true))
                throw new DashboardLayoutException("資訊板至少要保留一張可見卡片。");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<DashboardLayoutItem>(items.Count);
            foreach (var item in items)
            {
                var key = (item?.Key ?? string.Empty).Trim();
                if (key.Length == 0 || key.Length > 160)
                    throw new DashboardLayoutException("卡片識別碼不正確。");
                if (!seen.Add(key)) throw new DashboardLayoutException($"卡片「{key}」重複出現。");
                if (item.Column < 0 || item.Column >= ColumnCount || item.Row < 0 || item.Row > 40)
                    throw new DashboardLayoutException($"卡片「{key}」的位置超出範圍。");
                if (item.Width < 1 || item.Width > ColumnCount || item.Height < 1 || item.Height > 8 || item.Column + item.Width > ColumnCount)
                    throw new DashboardLayoutException($"卡片「{key}」的尺寸超出範圍。");

                result.Add(new DashboardLayoutItem
                {
                    Key = key,
                    Visible = item.Visible,
                    Column = item.Column,
                    Row = item.Row,
                    Width = item.Width,
                    Height = item.Height
                });
            }
            return result;
        }

        private static DashboardLayoutResponse CreateDefault(string profile)
        {
            var items = new List<DashboardLayoutItem>
            {
                Item("system-load", 0, 0, 4, 2),
                Item("activity-feed", 4, 0, 8, 2),
                Item("quota-mini", 0, 2, 4, 2),
                Item("cpu", 4, 2, 2, 1),
                Item("ram", 6, 2, 2, 1),
                Item("gpu", 8, 2, 2, 1),
                Item("vram", 10, 2, 2, 1),
                Item("disk", 4, 3, 2, 1),
                Item("network", 6, 3, 2, 1),
                Item("weather-io", 0, 4, 6, 2),
                Item("processes", 6, 4, 6, 2)
            };

            if (profile == "eink-portrait")
            {
                items = new List<DashboardLayoutItem>
                {
                    Item("system-load", 0, 0, 6, 2),
                    Item("quota-mini", 6, 0, 6, 2),
                    Item("activity-feed", 0, 2, 12, 3),
                    Item("cpu", 0, 5, 4, 1), Item("ram", 4, 5, 4, 1), Item("gpu", 8, 5, 4, 1),
                    Item("vram", 0, 6, 4, 1), Item("disk", 4, 6, 4, 1), Item("network", 8, 6, 4, 1),
                    Item("weather-io", 0, 7, 6, 3), Item("processes", 6, 7, 6, 3)
                };
            }

            return new DashboardLayoutResponse
            {
                Profile = profile,
                Revision = 0,
                UpdatedAt = null,
                Items = items
            };
        }

        private static DashboardLayoutItem Item(string key, int column, int row, int width, int height) => new DashboardLayoutItem
        {
            Key = key,
            Visible = true,
            Column = column,
            Row = row,
            Width = width,
            Height = height
        };

        private static DashboardLayoutResponse Clone(DashboardLayoutResponse value) => new DashboardLayoutResponse
        {
            Profile = value.Profile,
            Revision = value.Revision,
            UpdatedAt = value.UpdatedAt,
            Items = value.Items.Select(item => new DashboardLayoutItem
            {
                Key = item.Key,
                Visible = item.Visible,
                Column = item.Column,
                Row = item.Row,
                Width = item.Width,
                Height = item.Height
            }).ToArray()
        };

        private sealed class DashboardLayoutStore
        {
            public Dictionary<string, DashboardLayoutResponse> Profiles { get; set; } =
                new Dictionary<string, DashboardLayoutResponse>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public sealed class DashboardLayoutException : Exception
    {
        public DashboardLayoutException(string message) : base(message) { }
    }
}
