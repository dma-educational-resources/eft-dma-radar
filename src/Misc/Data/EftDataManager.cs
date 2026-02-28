using eft_dma_radar.Common.Misc.Data.TarkovMarket;
using eft_dma_radar.Common.UI;
using System.Collections.Frozen;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace eft_dma_radar.Common.Misc.Data
{
    public static class EftDataManager
    {
        private const string _dataFileName = "data.json";
        private const string _defaultDataFileName = "DEFAULT_DATA.json";

        private static readonly string _dataFile = Path.Combine(SharedProgram.ConfigPath.FullName, _dataFileName);
        private static readonly TimeSpan _defaultDataUpdateInterval = TimeSpan.FromHours(6);

        /// <summary>
        /// Master items dictionary - mapped via BSGID String.
        /// </summary>
        public static FrozenDictionary<string, TarkovMarketItem> AllItems { get; private set; }

        /// <summary>
        /// Master containers dictionary - mapped via BSGID String.
        /// </summary>
        public static FrozenDictionary<string, TarkovMarketItem> AllContainers { get; private set; }

        /// <summary>
        /// Quest Data for Tarkov.
        /// </summary>
        public static FrozenDictionary<string, TaskElement> TaskData { get; private set; }

        /// <summary>
        /// Map Data for Tarkov (extracts and transits).
        /// </summary>
        public static FrozenDictionary<string, MapElement> MapData { get; private set; }
        
        public static bool IsInitialized { get; set; } = false;

        #region Startup

        /// <summary>
        /// Call to start XMDataManager Module. ONLY CALL ONCE.
        /// </summary>
        /// <param name="loading">Loading UI Form.</param>
        /// <param name="defaultOnly">True if you want to load cached/default data only.</param>
        /// <returns></returns>
        public static async Task ModuleInitAsync(LoadingWindow loading, bool defaultOnly = false)
        {
            TarkovMarketData data = null;

            try
            {
                data = await LoadDataWithFallbacksAsync(loading, defaultOnly);
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Error loading data: {ex}");
                loading.UpdateStatus("Error loading data. Generating default data set...", loading.PercentComplete);

                data = CreateMinimalDataSet();
            }

            try
            {
                AllItems = data.Items.Where(x => !x.Tags?.Contains("Static Container") ?? false)
                    .DistinctBy(x => x.BsgId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(k => k.BsgId, v => v, StringComparer.OrdinalIgnoreCase)
                    .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

                AllContainers = data.Items.Where(x => x.Tags?.Contains("Static Container") ?? false)
                    .DistinctBy(x => x.BsgId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(k => k.BsgId, v => v, StringComparer.OrdinalIgnoreCase)
                    .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

                TaskData = data.Tasks
                    .DistinctBy(x => x.Id)
                    .ToDictionary(k => k.Id, v => v, StringComparer.OrdinalIgnoreCase)
                    .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

                MapData = data.Maps?
                    .Where(x => !string.IsNullOrEmpty(x.NameId))
                    .ToDictionary(k => k.NameId, v => v, StringComparer.OrdinalIgnoreCase)
                    .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase) 
                    ?? new Dictionary<string, MapElement>(StringComparer.OrdinalIgnoreCase).ToFrozenDictionary();

                IsInitialized = true;
                loading.UpdateStatus("Data initialization complete", loading.PercentComplete);
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Error processing data: {ex}");
                loading.UpdateStatus("Error processing data. Using empty data structures.", loading.PercentComplete);

                AllItems = new Dictionary<string, TarkovMarketItem>(StringComparer.OrdinalIgnoreCase).ToFrozenDictionary();
                AllContainers = new Dictionary<string, TarkovMarketItem>(StringComparer.OrdinalIgnoreCase).ToFrozenDictionary();
                TaskData = new Dictionary<string, TaskElement>(StringComparer.OrdinalIgnoreCase).ToFrozenDictionary();
                MapData = new Dictionary<string, MapElement>(StringComparer.OrdinalIgnoreCase).ToFrozenDictionary();
                IsInitialized = true;
            }
        }

        /// <summary>
        /// Creates a minimal valid data set to prevent crashes
        /// </summary>
        private static TarkovMarketData CreateMinimalDataSet()
        {
            XMLogging.WriteLine("Creating minimal data set as last resort");

            return new TarkovMarketData
            {
                Items = new List<TarkovMarketItem>(),
                Tasks = new List<TaskElement>()
            };
        }

        /// <summary>
        /// Attempts to load data from various sources, with clean fallbacks
        /// </summary>
        private static async Task<TarkovMarketData> LoadDataWithFallbacksAsync(LoadingWindow loading, bool defaultOnly)
        {
            TarkovMarketData data = null;
            JsonSerializerOptions jsonOptions = new JsonSerializerOptions()
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };

            if (File.Exists(_dataFile))
            {
                try
                {
                    loading.UpdateStatus("Loading cached data file...", loading.PercentComplete);
                    string json = await File.ReadAllTextAsync(_dataFile);
                    data = JsonSerializer.Deserialize<TarkovMarketData>(json, jsonOptions);

                    if (data != null && data.Items != null && data.Tasks != null)
                    {
                        loading.UpdateStatus("Cached data loaded successfully", loading.PercentComplete);

                        if (!defaultOnly && IsDataFileOutdated(_dataFile, _defaultDataUpdateInterval))
                        {
                            loading.UpdateStatus($"Cached data is outdated (older than {_defaultDataUpdateInterval.TotalHours} hours), will update after initialization", loading.PercentComplete);

                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(4000);
                                await UpdateDataFileAsync();
                            });
                        }
                        else
                        {
                            loading.UpdateStatus($"Using cached data (updated in the last {_defaultDataUpdateInterval.TotalHours} hours)", loading.PercentComplete);
                        }

                        return data;
                    }
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"Error loading cached data: {ex}");
                    loading.UpdateStatus("Cached data is invalid, will create new data", loading.PercentComplete);

                    try
                    {
                        File.Delete(_dataFile);
                    }
                    catch
                    {
                        // Ignore deletion errors
                    }
                }
            }

            if (!defaultOnly)
            {
                try
                {
                    loading.UpdateStatus("Fetching data from Tarkov.Dev API...", loading.PercentComplete);
                    string json = await TarkovMarketJob.GetUpdatedMarketDataAsync();

                    if (!string.IsNullOrEmpty(json))
                    {
                        data = JsonSerializer.Deserialize<TarkovMarketData>(json, jsonOptions);

                        if (data != null && data.Items != null && data.Tasks != null)
                        {
                            loading.UpdateStatus("API data fetched successfully", loading.PercentComplete);

                            try
                            {
                                await File.WriteAllTextAsync(_dataFile, json);
                                loading.UpdateStatus("API data saved to cache", loading.PercentComplete);
                            }
                            catch (Exception ex)
                            {
                                XMLogging.WriteLine($"Warning: Could not save API data to file: {ex}");
                            }

                            return data;
                        }
                    }
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"Error fetching API data: {ex}");
                    loading.UpdateStatus("API fetch failed, falling back to embedded data", loading.PercentComplete);
                }
            }

            try
            {
                loading.UpdateStatus("Loading embedded default data...", loading.PercentComplete);

                var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var defaultDataPath = Path.Combine(assemblyDir, _defaultDataFileName);

                if (File.Exists(defaultDataPath) && !IsDataFileOutdated(defaultDataPath, _defaultDataUpdateInterval))
                {
                    loading.UpdateStatus($"Using embedded default data (updated in the last {_defaultDataUpdateInterval.TotalHours} hours)", loading.PercentComplete);
                }
                else if (!defaultOnly)
                {
                    loading.UpdateStatus($"Default data is outdated (older than {_defaultDataUpdateInterval.TotalHours} hours), attempting to update", loading.PercentComplete);
                    await UpdateDefaultDataAsync(loading);
                }

                string json = await GetDefaultDataAsync();

                if (!string.IsNullOrEmpty(json))
                {
                    data = JsonSerializer.Deserialize<TarkovMarketData>(json, jsonOptions);

                    if (data != null && data.Items != null && data.Tasks != null)
                    {
                        loading.UpdateStatus("Embedded default data loaded successfully", loading.PercentComplete);

                        try
                        {
                            await File.WriteAllTextAsync(_dataFile, json);
                            loading.UpdateStatus("Default data saved to cache", loading.PercentComplete);
                        }
                        catch (Exception ex)
                        {
                            XMLogging.WriteLine($"Warning: Could not save default data to file: {ex}");
                        }

                        return data;
                    }
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Error loading embedded data: {ex}");
                loading.UpdateStatus("Embedded data load failed, using minimal dataset", loading.PercentComplete);
            }

            return CreateMinimalDataSet();
        }

        /// <summary>
        /// Checks if the data file is outdated based on the specified interval
        /// </summary>
        private static bool IsDataFileOutdated(string filePath, TimeSpan updateInterval)
        {
            try
            {
                if (!File.Exists(filePath))
                    return true;

                var lastModified = File.GetLastWriteTime(filePath);
                return (DateTime.Now - lastModified) > updateInterval;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Error checking file age: {ex}");
                return true;
            }
        }

        /// <summary>
        /// Updates the embedded default data if needed
        /// </summary>
        private static async Task UpdateDefaultDataAsync(LoadingWindow loading)
        {
            try
            {
                var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var defaultDataPath = Path.Combine(assemblyDir, _defaultDataFileName);

                if (IsDataFileOutdated(defaultDataPath, _defaultDataUpdateInterval))
                {
                    loading.UpdateStatus("Updating embedded default data...", loading.PercentComplete);

                    try
                    {
                        string json = await TarkovMarketJob.GetUpdatedMarketDataAsync();

                        if (!string.IsNullOrEmpty(json))
                        {
                            var jsonOptions = new JsonSerializerOptions()
                            {
                                PropertyNameCaseInsensitive = true,
                                NumberHandling = JsonNumberHandling.AllowReadingFromString
                            };

                            var testData = JsonSerializer.Deserialize<TarkovMarketData>(json, jsonOptions);

                            if (testData != null && testData.Items != null && testData.Tasks != null)
                            {
                                await File.WriteAllTextAsync(defaultDataPath, json);
                                loading.UpdateStatus("Default data updated successfully", loading.PercentComplete);
                                return;
                            }
                        }

                        loading.UpdateStatus("Failed to update default data, will use existing data", loading.PercentComplete);
                    }
                    catch (Exception ex)
                    {
                        XMLogging.WriteLine($"Error updating default data: {ex}");
                        loading.UpdateStatus("Error updating default data, will use existing data", loading.PercentComplete);
                    }
                }
                else
                {
                    loading.UpdateStatus($"Default data is up to date (updated in the last {_defaultDataUpdateInterval.TotalHours} hours)", loading.PercentComplete);
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Error in UpdateDefaultDataAsync: {ex}");
                loading.UpdateStatus("Error checking default data, continuing with existing data", loading.PercentComplete);
            }
        }

        /// <summary>
        /// Updates the data file asynchronously
        /// </summary>
        public static async Task<bool> UpdateDataFileAsync()
        {
            try
            {
                XMLogging.WriteLine("Starting background data update");
                string json = await TarkovMarketJob.GetUpdatedMarketDataAsync();

                if (!string.IsNullOrEmpty(json))
                {
                    var jsonOptions = new JsonSerializerOptions()
                    {
                        WriteIndented = true,
                        PropertyNameCaseInsensitive = true,
                        NumberHandling = JsonNumberHandling.AllowReadingFromString
                    };

                    var data = JsonSerializer.Deserialize<TarkovMarketData>(json, jsonOptions);

                    if (data != null && data.Items != null && data.Tasks != null)
                    {
                        await File.WriteAllTextAsync(_dataFile, json);

                        AllItems = data.Items.Where(x => !x.Tags?.Contains("Static Container") ?? false)
                            .DistinctBy(x => x.BsgId, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(k => k.BsgId, v => v, StringComparer.OrdinalIgnoreCase)
                            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

                        AllContainers = data.Items.Where(x => x.Tags?.Contains("Static Container") ?? false)
                            .DistinctBy(x => x.BsgId, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(k => k.BsgId, v => v, StringComparer.OrdinalIgnoreCase)
                            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

                        TaskData = data.Tasks
                            .DistinctBy(x => x.Id)
                            .ToDictionary(k => k.Id, v => v, StringComparer.OrdinalIgnoreCase)
                            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

                        XMLogging.WriteLine("Background data update successful");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Background data update failed: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Checks if an update of data is available and updates if needed
        /// </summary>
        public static async Task<bool> CheckForDataUpdateAsync()
        {
            try
            {
                if (IsDataFileOutdated(_dataFile, _defaultDataUpdateInterval))
                {
                    XMLogging.WriteLine($"Data file is outdated (older than {_defaultDataUpdateInterval.TotalHours} hours), updating...");
                    return await UpdateDataFileAsync();
                }

                XMLogging.WriteLine($"Data file is up to date (updated in the last {_defaultDataUpdateInterval.TotalHours} hours)");
                return false;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Error checking for data update: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Gets the default embedded data
        /// </summary>
        private static async Task<string> GetDefaultDataAsync()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("eft_dma_radar.DEFAULT_DATA.json"))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException("Embedded default data not found");
                }

                var data = new byte[stream.Length];
                await stream.ReadExactlyAsync(data);
                return Encoding.UTF8.GetString(data);
            }
        }

        /// <summary>
        /// Creates an updated DEFAULT_DATA.json file from the latest Tarkov.Dev data.
        /// This should be run manually when you want to update the embedded resource.
        /// </summary>
        /// <param name="outputPath">The path where DEFAULT_DATA.json will be saved. If null, saves to the executable directory.</param>
        /// <returns>The path to the created file</returns>
        public static async Task<string> CreateDefaultDataFileAsync(string outputPath = null)
        {
            try
            {
                var json = await TarkovMarketJob.GetUpdatedMarketDataAsync();

                if (string.IsNullOrEmpty(json))
                    throw new InvalidOperationException("Failed to retrieve updated market data.");

                outputPath ??= Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), _defaultDataFileName);

                await File.WriteAllTextAsync(outputPath, json);

                return outputPath;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Error creating default data file: {ex}");
                throw;
            }
        }
        #endregion

        #region Types
        public sealed class TarkovMarketData
        {
            [JsonPropertyName("items")]
            public List<TarkovMarketItem> Items { get; set; } = new List<TarkovMarketItem>();

            [JsonPropertyName("tasks")]
            public List<TaskElement> Tasks { get; set; } = new List<TaskElement>();

            [JsonPropertyName("maps")]
            public List<MapElement> Maps { get; set; } = new List<MapElement>();
        }

        /// <summary>
        /// Map data element containing extracts and transits.
        /// </summary>
        public class MapElement
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("nameId")]
            public string NameId { get; set; }

            [JsonPropertyName("extracts")]
            public List<ExtractElement> Extracts { get; set; } = new();

            [JsonPropertyName("transits")]
            public List<TransitElement> Transits { get; set; } = new();
        }

        public class ExtractElement
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("faction")]
            public string Faction { get; set; }

            [JsonPropertyName("position")]
            public MapPositionElement Position { get; set; }
        }

        public class TransitElement
        {
            [JsonPropertyName("description")]
            public string Description { get; set; }

            [JsonPropertyName("position")]
            public MapPositionElement Position { get; set; }
        }

        public class MapPositionElement
        {
            [JsonPropertyName("x")]
            public float X { get; set; }

            [JsonPropertyName("y")]
            public float Y { get; set; }

            [JsonPropertyName("z")]
            public float Z { get; set; }

            public System.Numerics.Vector3 ToVector3() => new(X, Y, Z);
        }

        public partial class TaskElement
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("kappaRequired")]
            public bool KappaRequired { get; set; }

            [JsonPropertyName("objectives")]
            public List<ObjectiveElement> Objectives { get; set; } = new List<ObjectiveElement>();

            public partial class ObjectiveElement
            {
                [JsonPropertyName("id")]
                public string Id { get; set; }

                [JsonPropertyName("type")]
                public string Type { get; set; }

                [JsonPropertyName("optional")]
                public bool Optional { get; set; }

                [JsonPropertyName("description")]
                public string Description { get; set; }

                [JsonPropertyName("requiredKeys")]
                public List<List<MarkerItemClass>> RequiredKeys { get; set; }

                [JsonPropertyName("maps")]
                public List<BasicDataElement> Maps { get; set; }

                [JsonPropertyName("zones")]
                public List<ZoneElement> Zones { get; set; }

                [JsonPropertyName("count")]
                public int Count { get; set; }

                [JsonPropertyName("foundInRaid")]
                public bool FoundInRaid { get; set; }

                [JsonPropertyName("item")]
                public MarkerItemClass Item { get; set; }

                [JsonPropertyName("questItem")]
                public ObjectiveQuestItem QuestItem { get; set; }

                [JsonPropertyName("markerItem")]
                public MarkerItemClass MarkerItem { get; set; }

                public partial class MarkerItemClass
                {
                    [JsonPropertyName("id")]
                    public string Id { get; set; }

                    [JsonPropertyName("name")]
                    public string Name { get; set; }

                    [JsonPropertyName("shortName")]
                    public string ShortName { get; set; }
                }

                public partial class ObjectiveQuestItem
                {
                    [JsonPropertyName("id")]
                    public string Id { get; set; }

                    [JsonPropertyName("name")]
                    public string Name { get; set; }

                    [JsonPropertyName("shortName")]
                    public string ShortName { get; set; }

                    [JsonPropertyName("normalizedName")]
                    public string NormalizedName { get; set; }

                    [JsonPropertyName("description")]
                    public string Description { get; set; }
                }

                public partial class ZoneElement
                {
                    [JsonPropertyName("id")]
                    public string Id { get; set; }

                    [JsonPropertyName("outline")]
                    public List<PositionElement> Outline { get; set; }

                    [JsonPropertyName("position")]
                    public PositionElement Position { get; set; }

                    [JsonPropertyName("map")]
                    public BasicDataElement Map { get; set; }
                }

                public partial class BasicDataElement
                {
                    [JsonPropertyName("id")]
                    public string Id { get; set; }

                    [JsonPropertyName("normalizedName")]
                    public string NormalizedName { get; set; }

                    [JsonPropertyName("name")]
                    public string Name { get; set; }
                }

                public partial class PositionElement
                {
                    [JsonPropertyName("y")]
                    public float Y { get; set; }

                    [JsonPropertyName("x")]
                    public float X { get; set; }

                    [JsonPropertyName("z")]
                    public float Z { get; set; }
                }
            }
        }
        #endregion
    }
}