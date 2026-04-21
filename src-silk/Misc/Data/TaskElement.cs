namespace eft_dma_radar.Silk.Misc.Data
{
    /// <summary>
    /// Quest/task data from the tarkov.dev API (embedded in DEFAULT_DATA.json).
    /// </summary>
    internal sealed class TaskElement
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("trader")]
        public TraderRef? Trader { get; set; }

        [JsonPropertyName("map")]
        public BasicRef? Map { get; set; }

        [JsonPropertyName("kappaRequired")]
        public bool KappaRequired { get; set; }

        [JsonPropertyName("objectives")]
        public List<ObjectiveElement>? Objectives { get; set; }

        [JsonPropertyName("taskRequirements")]
        public List<TaskRequirementElement>? TaskRequirements { get; set; }

        internal sealed class TraderRef
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;
        }

        internal sealed class BasicRef
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("normalizedName")]
            public string NormalizedName { get; set; } = string.Empty;
        }

        internal sealed class TaskRequirementElement
        {
            [JsonPropertyName("task")]
            public TaskRef? Task { get; set; }

            [JsonPropertyName("status")]
            public List<string>? Status { get; set; }
        }

        internal sealed class TaskRef
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;
        }

        internal sealed class ObjectiveElement
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("type")]
            public string Type { get; set; } = string.Empty;

            [JsonPropertyName("optional")]
            public bool Optional { get; set; }

            [JsonPropertyName("description")]
            public string Description { get; set; } = string.Empty;

            [JsonPropertyName("requiredKeys")]
            public List<List<ItemRef>>? RequiredKeys { get; set; }

            [JsonPropertyName("maps")]
            public List<BasicRef>? Maps { get; set; }

            [JsonPropertyName("zones")]
            public List<ZoneElement>? Zones { get; set; }

            [JsonPropertyName("count")]
            public int Count { get; set; }

            [JsonPropertyName("foundInRaid")]
            public bool FoundInRaid { get; set; }

            [JsonPropertyName("item")]
            public ItemRef? Item { get; set; }

            [JsonPropertyName("questItem")]
            public ItemRef? QuestItem { get; set; }

            [JsonPropertyName("markerItem")]
            public ItemRef? MarkerItem { get; set; }
        }

        internal sealed class ItemRef
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("shortName")]
            public string ShortName { get; set; } = string.Empty;
        }

        internal sealed class ZoneElement
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("outline")]
            public List<PositionElement>? Outline { get; set; }

            [JsonPropertyName("position")]
            public PositionElement? Position { get; set; }

            [JsonPropertyName("map")]
            public BasicRef? Map { get; set; }
        }

        internal sealed class PositionElement
        {
            [JsonPropertyName("x")]
            public float X { get; set; }

            [JsonPropertyName("y")]
            public float Y { get; set; }

            [JsonPropertyName("z")]
            public float Z { get; set; }
        }
    }
}
