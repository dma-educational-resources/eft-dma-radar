using SkiaSharp;
using System.Text.Json.Serialization;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites.Chams
{
    public sealed class ChamsConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        [JsonPropertyName("entityChams")]
        public Dictionary<ChamsEntityType, EntityChamsSettings> EntityChams { get; set; } = new()
        {
            { ChamsEntityType.PMC, new EntityChamsSettings() },
            { ChamsEntityType.Teammate, new EntityChamsSettings() },
            { ChamsEntityType.AI, new EntityChamsSettings() },
            { ChamsEntityType.AimbotTarget, new EntityChamsSettings() }
        };

        public class EntityChamsSettings
        {
            [JsonPropertyName("enabled")]
            public bool Enabled { get; set; } = false;

            [JsonPropertyName("mode")]
            public ChamsMode Mode { get; set; } = ChamsMode.Basic;

            [JsonPropertyName("visibleColor")]
            public string VisibleColor { get; set; } = SKColor.Parse("FF32D42D").ToString();

            [JsonPropertyName("invisibleColor")]
            public string InvisibleColor { get; set; } = SKColor.Parse("FFCD251E").ToString();

            [JsonPropertyName("revertOnDeath")]
            public bool RevertOnDeath { get; set; } = true;

            [JsonPropertyName("clothingChamsEnabled")]
            public bool ClothingChamsEnabled { get; set; } = true;

            [JsonPropertyName("clothingChamsMode")]
            public ChamsMode ClothingChamsMode { get; set; } = ChamsMode.Basic;

            [JsonPropertyName("gearChamsEnabled")]
            public bool GearChamsEnabled { get; set; } = true;

            [JsonPropertyName("gearChamsMode")]
            public ChamsMode GearChamsMode { get; set; } = ChamsMode.Basic;

            [JsonPropertyName("deathMaterialEnabled")]
            public bool DeathMaterialEnabled { get; set; } = false;

            [JsonPropertyName("deathMaterialMode")]
            public ChamsMode DeathMaterialMode { get; set; } = ChamsMode.VisCheckFlat;

            [JsonPropertyName("deathMaterialColor")]
            public string DeathMaterialColor { get; set; } = SKColor.Parse("FF808080").ToString(); // Gray for death

            [JsonPropertyName("materialColors")]
            public Dictionary<ChamsMode, MaterialColorSettings> MaterialColors { get; set; } = new()
            {
                { ChamsMode.WireFrame, new MaterialColorSettings
                    {
                        VisibleColor = SKColor.Parse("FF00FF00").ToString(),
                        InvisibleColor = SKColor.Parse("FFFF0000").ToString()
                    }
                },
                { ChamsMode.VisCheckGlow, new MaterialColorSettings
                    {
                        VisibleColor = SKColor.Parse("FF32D42D").ToString(),
                        InvisibleColor = SKColor.Parse("FFCD251E").ToString()
                    }
                },
                { ChamsMode.VisCheckFlat, new MaterialColorSettings
                    {
                        VisibleColor = SKColor.Parse("FF0080FF").ToString(),
                        InvisibleColor = SKColor.Parse("FFFF8000").ToString()
                    }
                }
            };
        }

        public class MaterialColorSettings
        {
            [JsonPropertyName("visibleColor")]
            public string VisibleColor { get; set; } = SKColor.Parse("FF32D42D").ToString();

            [JsonPropertyName("invisibleColor")]
            public string InvisibleColor { get; set; } = SKColor.Parse("FFCD251E").ToString();
        }

        public void InitializeDefaults()
        {
            var allEntityTypes = new[]
            {
                ChamsEntityType.PMC,
                ChamsEntityType.Teammate,
                ChamsEntityType.AI,
                ChamsEntityType.AimbotTarget
            };

            foreach (var entityType in allEntityTypes)
            {
                if (!EntityChams.ContainsKey(entityType))
                {
                    EntityChams[entityType] = new EntityChamsSettings();
                }

                var settings = EntityChams[entityType];
                if (settings.MaterialColors == null)
                {
                    settings.MaterialColors = new Dictionary<ChamsMode, MaterialColorSettings>
                    {
                        { ChamsMode.WireFrame, new MaterialColorSettings
                            {
                                VisibleColor = SKColor.Parse("FF00FF00").ToString(),
                                InvisibleColor = SKColor.Parse("FFFF0000").ToString()
                            }
                        },
                        { ChamsMode.VisCheckGlow, new MaterialColorSettings
                            {
                                VisibleColor = SKColor.Parse("FF32D42D").ToString(),
                                InvisibleColor = SKColor.Parse("FFCD251E").ToString()
                            }
                        },
                        { ChamsMode.VisCheckFlat, new MaterialColorSettings
                            {
                                VisibleColor = SKColor.Parse("FF0080FF").ToString(),
                                InvisibleColor = SKColor.Parse("FFFF8000").ToString()
                            }
                        }
                    };
                }
                else
                {
                    var materialModes = new[] { ChamsMode.WireFrame, ChamsMode.VisCheckGlow, ChamsMode.VisCheckFlat };
                    foreach (var mode in materialModes)
                    {
                        if (!settings.MaterialColors.ContainsKey(mode))
                        {
                            settings.MaterialColors[mode] = new MaterialColorSettings();
                        }
                    }
                }
            }
        }

        public EntityChamsSettings GetEntitySettings(ChamsEntityType entityType)
        {
            if (!EntityChams.ContainsKey(entityType))
                EntityChams[entityType] = new EntityChamsSettings();

            return EntityChams[entityType];
        }

        public MaterialColorSettings GetMaterialColorSettings(ChamsEntityType entityType, ChamsMode materialMode)
        {
            var entitySettings = GetEntitySettings(entityType);

            if (entitySettings.MaterialColors == null)
                entitySettings.MaterialColors = new Dictionary<ChamsMode, MaterialColorSettings>();

            if (!entitySettings.MaterialColors.ContainsKey(materialMode))
                entitySettings.MaterialColors[materialMode] = new MaterialColorSettings();

            return entitySettings.MaterialColors[materialMode];
        }
    }
}