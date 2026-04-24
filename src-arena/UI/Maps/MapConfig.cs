using System.Collections.Frozen;
using eft_dma_radar.Arena.GameWorld.Exits;

namespace eft_dma_radar.Arena.UI.Maps
{
    /// <summary>
    /// JSON-deserializable map configuration.
    /// </summary>
    internal sealed class MapConfig
    {
        [JsonPropertyName("mapID")]
        public List<string> MapID { get; init; } = [];

        [JsonPropertyName("x")]
        public float X { get; init; }

        [JsonPropertyName("y")]
        public float Y { get; init; }

        [JsonPropertyName("scale")]
        public float Scale { get; init; }

        [JsonPropertyName("svgScale")]
        public float SvgScale { get; init; } = 1f;

        [JsonPropertyName("disableDimming")]
        public bool DisableDimming { get; init; }

        [JsonPropertyName("mapLayers")]
        public List<MapLayer> MapLayers { get; init; } = [];

        /// <summary>
        /// Display name derived from the primary map ID. Delegates to the shared
        /// Arena <see cref="MapNames"/> dictionary so names aren't duplicated.
        /// </summary>
        [JsonIgnore]
        public string Name => MapID.Count > 0 ? MapNames.GetDisplayName(MapID[0]) : "Unknown";
    }

    /// <summary>
    /// A single height-constrained layer within a map.
    /// </summary>
    internal sealed class MapLayer
    {
        [JsonPropertyName("minHeight")]
        public float? MinHeight { get; init; }

        [JsonPropertyName("maxHeight")]
        public float? MaxHeight { get; init; }

        [JsonPropertyName("filename")]
        public string Filename { get; init; } = string.Empty;

        [JsonPropertyName("dimBaseLayer")]
        public bool DimBaseLayer { get; init; }

        /// <summary>
        /// A layer is the base layer when it has no height constraints.
        /// </summary>
        [JsonIgnore]
        public bool IsBaseLayer => MinHeight is null && MaxHeight is null;

        [JsonIgnore]
        public float SortHeight => MinHeight ?? float.MinValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsHeightInRange(float height)
        {
            if (IsBaseLayer)
                return true;
            if (MinHeight.HasValue && height < MinHeight.Value)
                return false;
            if (MaxHeight.HasValue && height > MaxHeight.Value)
                return false;
            return true;
        }
    }
}
