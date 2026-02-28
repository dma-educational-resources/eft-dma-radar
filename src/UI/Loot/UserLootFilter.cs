using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;

namespace eft_dma_radar.UI.LootFilters
{
    public sealed class UserLootFilter
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;

        [JsonInclude]
        [JsonPropertyName("entries")]
        public List<LootFilterEntry> Entries { get; init; } = new();
    }

    public static class LootFilterManager
    {
        public static GroupedLootFilterConfig CurrentGroups { get; private set; } = new();

        public static string GroupedLootFilterFilePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar", "lootfilters.json");

        public static void Load()
        {
            if (File.Exists(GroupedLootFilterFilePath))
            {
                try
                {
                    var json = File.ReadAllText(GroupedLootFilterFilePath);
                    CurrentGroups = JsonSerializer.Deserialize<GroupedLootFilterConfig>(json) ?? new();
                }
                catch
                {
                    XMLogging.WriteLine("[LootFilterManager] Failed to load grouped loot filter config. Using defaults.");
                    CurrentGroups = new();
                }
            }
            else
            {
                CurrentGroups = new();
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(GroupedLootFilterFilePath)!);
                var json = JsonSerializer.Serialize(CurrentGroups, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GroupedLootFilterFilePath, json);
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[LootFilterManager] Failed to save grouped loot filter config: {ex.Message}");
            }
        }
    }

    public sealed class LootFilterGroup : INotifyPropertyChanged
    {
        private int index;
        private string name = string.Empty;
        private bool notify;
        private int notTime;
        private bool enabled;
        private bool isStatic = true;
        private List<GroupedLootFilterEntry> items = new();

        [JsonPropertyName("index")]
        public int Index
        {
            get => index;
            set => SetField(ref index, value);
        }

        [JsonPropertyName("notTime")]
        public int NotTime
        {
            get => notTime;
            set => SetField(ref notTime, value);
        }
        [JsonPropertyName("name")]
        public string Name
        {
            get => name;
            set => SetField(ref name, value);
        }

        [JsonPropertyName("notify")]
        public bool Notify
        {
            get => notify;
            set => SetField(ref notify, value);
        }

        [JsonPropertyName("enabled")]
        public bool Enabled
        {
            get => enabled;
            set => SetField(ref enabled, value);
        }
        [JsonPropertyName("isStatic")]
        public bool IsStatic
        {
            get => isStatic;
            set => SetField(ref isStatic, value);
        }

        [JsonPropertyName("items")]
        public List<GroupedLootFilterEntry> Items
        {
            get => items;
            set => SetField(ref items, value);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string prop = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string prop = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(prop);
            return true;
        }
    }

    public sealed class GroupedLootFilterEntry : INotifyPropertyChanged
    {
        private string itemID = string.Empty;
        private string color = "#FF2CF243";
        private bool notify = false;
        private bool enabled = true;
        private bool isStatic = true;
        private string note = string.Empty;
        private bool blacklist = false;

        [JsonPropertyName("itemID")]
        public string ItemID
        {
            get => itemID;
            set => SetField(ref itemID, value);
        }

        [JsonIgnore]
        public string Name
        {
            get
            {
                if (EftDataManager.AllItems.TryGetValue(ItemID, out var item))
                {
                    // If item is ammo, use full name
                    if (item.IsAmmo) // or whatever your ammo parent is
                        return item.Name;

                    // Otherwise use ShortName
                    return item.ShortName;
                }

                return ItemID;
            }
        }

        [JsonPropertyName("color")]
        public string Color
        {
            get => color;
            set => SetField(ref color, value);
        }

        [JsonPropertyName("notify")]
        public bool Notify
        {
            get => notify;
            set => SetField(ref notify, value);
        }

        [JsonPropertyName("enabled")]
        public bool Enabled
        {
            get => enabled;
            set => SetField(ref enabled, value);
        }

        [JsonPropertyName("isStatic")]
        public bool IsStatic
        {
            get => isStatic;
            set => SetField(ref isStatic, value);
        }

        [JsonPropertyName("note")]
        public string Note
        {
            get => note;
            set => SetField(ref note, value);
        }

        [JsonIgnore]
        public bool HasNote => !string.IsNullOrWhiteSpace(Note);

        [JsonPropertyName("blacklist")]
        public bool Blacklist
        {
            get => blacklist;
            set => SetField(ref blacklist, value);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);

            // When Note changes, also notify HasNote property change
            if (propertyName == nameof(Note))
                OnPropertyChanged(nameof(HasNote));

            return true;
        }
    }

    public sealed class GroupedLootFilterConfig
    {
        [JsonPropertyName("groups")]
        public List<LootFilterGroup> Groups { get; set; } = new();
    }
}