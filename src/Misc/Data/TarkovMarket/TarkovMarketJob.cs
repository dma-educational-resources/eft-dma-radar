using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using System.Net;
using System.Text.Json;

namespace eft_dma_radar.Common.Misc.Data.TarkovMarket
{
    public static class TarkovMarketJob
    {

        public static async Task<string> GetUpdatedMarketDataAsync()
        {
            try
            {
                var data = await TarkovDevCore.QueryTarkovDevAsync();
                var result = new TarkovMarketData()
                {
                    Items = ParseMarketData(data),
                    Tasks = data.Data.Tasks,
                    Maps = ParseMapsData(data)
                };
                return JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"{nameof(TarkovMarketJob)} [FAIL]: {ex}");
                throw;
            }
        }

        private static List<OutgoingItem> ParseMarketData(TarkovDevQuery data)
        {
            var outgoingItems = new List<OutgoingItem>();
            foreach (var item in data.Data.Items)
            {
                int slots = item.Width * item.Height;
                outgoingItems.Add(new OutgoingItem()
                {
                    ID = item.Id,
                    ShortName = item.ShortName,
                    Name = item.Name,
                    Categories = item.Categories?.Select(x => x.Name)?.ToList() ?? new(),
                    TraderPrice = item.HighestVendorPrice,
                    FleaPrice = item.OptimalFleaPrice,
                    Slots = item.Width * item.Height,
                    IconLink = item.IconLink,
                    IconLinkFallback = item.IconLinkFallback,
                    ImageLink = item.ImageLink,
                    Caliber = item.Properties?.Caliber  // <-- fix here
                });
                
            }
            foreach (var questItem in data.Data.QuestItems)
            {
                outgoingItems.Add(new OutgoingItem()
                {
                    ID = questItem.Id,
                    ShortName = $"Q_{questItem.ShortName}",
                    Name = $"Q_{questItem.ShortName}",
                    Categories = new() { "Quest Item" },
                    TraderPrice = -1,
                    FleaPrice = -1,
                    Slots = 1
                });
            }
            foreach (var container in data.Data.LootContainers)
            {
                outgoingItems.Add(new OutgoingItem()
                {
                    ID = container.Id,
                    ShortName = container.Name,
                    Name = container.NormalizedName,
                    Categories = new() { "Static Container" },
                    TraderPrice = -1,
                    FleaPrice = -1,
                    Slots = 1
                });
            }
            return outgoingItems;
        }

        private static List<OutgoingMap> ParseMapsData(TarkovDevQuery data)
        {
            if (data.Data.Maps == null)
                return new List<OutgoingMap>();

            return data.Data.Maps.Select(m => new OutgoingMap
            {
                Name = m.Name,
                NameId = m.NameId,
                Extracts = m.Extracts?.Select(e => new OutgoingExtract
                {
                    Name = e.Name,
                    Faction = e.Faction,
                    Position = e.Position != null ? new OutgoingPosition { X = e.Position.X, Y = e.Position.Y, Z = e.Position.Z } : null
                }).ToList() ?? new List<OutgoingExtract>(),
                Transits = m.Transits?.Select(t => new OutgoingTransit
                {
                    Description = t.Description,
                    Position = t.Position != null ? new OutgoingPosition { X = t.Position.X, Y = t.Position.Y, Z = t.Position.Z } : null
                }).ToList() ?? new List<OutgoingTransit>()
            }).ToList();
        }

        #region Outgoing JSON
        private sealed class TarkovMarketData
        {
            [JsonPropertyName("items")]
            public List<OutgoingItem> Items { get; set; }
            [JsonPropertyName("tasks")]
            public List<TaskElement> Tasks { get; set; }
            [JsonPropertyName("maps")]
            public List<OutgoingMap> Maps { get; set; }
        }

        private sealed class OutgoingMap
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }
            [JsonPropertyName("nameId")]
            public string NameId { get; set; }
            [JsonPropertyName("extracts")]
            public List<OutgoingExtract> Extracts { get; set; }
            [JsonPropertyName("transits")]
            public List<OutgoingTransit> Transits { get; set; }
        }

        private sealed class OutgoingExtract
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }
            [JsonPropertyName("faction")]
            public string Faction { get; set; }
            [JsonPropertyName("position")]
            public OutgoingPosition Position { get; set; }
        }

        private sealed class OutgoingTransit
        {
            [JsonPropertyName("description")]
            public string Description { get; set; }
            [JsonPropertyName("position")]
            public OutgoingPosition Position { get; set; }
        }

        private sealed class OutgoingPosition
        {
            [JsonPropertyName("x")]
            public float X { get; set; }
            [JsonPropertyName("y")]
            public float Y { get; set; }
            [JsonPropertyName("z")]
            public float Z { get; set; }
        }

        private sealed class OutgoingItem
        {
            [JsonPropertyName("bsgID")]
            public string ID { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("shortName")]
            public string ShortName { get; set; }

            [JsonPropertyName("price")]
            public long TraderPrice { get; set; }

            [JsonPropertyName("fleaPrice")]
            public long FleaPrice { get; set; }

            [JsonPropertyName("slots")]
            public int Slots { get; set; }

            [JsonPropertyName("categories")]
            public List<string> Categories { get; set; }

            [JsonPropertyName("iconLink")]
            public string IconLink { get; set; }

            [JsonPropertyName("iconLinkFallback")]
            public string IconLinkFallback { get; set; }

            [JsonPropertyName("imageLink")]
            public string ImageLink { get; set; }

            [JsonPropertyName("caliber")]
            public string Caliber { get; set; }
        }

        #endregion
    }
}
