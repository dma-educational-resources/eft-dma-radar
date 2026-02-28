using System.Text.Json.Serialization;

namespace eft_dma_radar.Common.Misc.Data.TarkovMarket
{
    public partial class TarkovDevQuery
    {
        [JsonPropertyName("warnings")]
        public List<WarningMessage> Warnings { get; set; }

        [JsonPropertyName("data")]
        public TarkovDevData Data { get; set; }

        public partial class TarkovDevData
        {
            [JsonPropertyName("maps")]
            public List<ApiMapElement> Maps { get; set; }

            [JsonPropertyName("questItems")]
            public List<QuestItemElement> QuestItems { get; set; }

            [JsonPropertyName("lootContainers")]
            public List<BasicDataElement> LootContainers { get; set; }

            [JsonPropertyName("tasks")]
            public List<TaskElement> Tasks { get; set; }

            [JsonPropertyName("items")]
            public List<ItemElement> Items { get; set; }
        }

        public partial class ApiMapElement
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("nameId")]
            public string NameId { get; set; }

            [JsonPropertyName("extracts")]
            public List<ApiExtractElement> Extracts { get; set; }

            [JsonPropertyName("transits")]
            public List<ApiTransitElement> Transits { get; set; }
        }

        public partial class ApiExtractElement
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("faction")]
            public string Faction { get; set; }

            [JsonPropertyName("position")]
            public ApiPositionElement Position { get; set; }
        }

        public partial class ApiTransitElement
        {
            [JsonPropertyName("description")]
            public string Description { get; set; }

            [JsonPropertyName("position")]
            public ApiPositionElement Position { get; set; }
        }

        public partial class ApiPositionElement
        {
            [JsonPropertyName("x")]
            public float X { get; set; }

            [JsonPropertyName("y")]
            public float Y { get; set; }

            [JsonPropertyName("z")]
            public float Z { get; set; }
        }
        public partial class WarningMessage
        {
            [JsonPropertyName("message")]
            public string Message { get; set; }
        }
    }

    public partial class ItemElement
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
    
        [JsonPropertyName("name")]
        public string Name { get; set; }
    
        [JsonPropertyName("shortName")]
        public string ShortName { get; set; }
    
        [JsonPropertyName("width")]
        public int Width { get; set; }
    
        [JsonPropertyName("height")]
        public int Height { get; set; }
    
        [JsonPropertyName("basePrice")]
        public long BasePrice { get; set; }
    
        [JsonPropertyName("avg24hPrice")]
        public long? Avg24HPrice { get; set; }
    
        [JsonPropertyName("categories")]
        public List<CategoryElement> Categories { get; set; }
    
        [JsonPropertyName("sellFor")]
        public List<SellForElement> SellFor { get; set; }
    
        [JsonPropertyName("historicalPrices")]
        public List<HistoricalPrice> HistoricalPrices { get; set; }
    
        [JsonPropertyName("iconLink")]
        public string IconLink { get; set; }
    
        [JsonPropertyName("iconLinkFallback")]
        public string IconLinkFallback { get; set; }
    
        [JsonPropertyName("imageLink")]
        public string ImageLink { get; set; }
    
        [JsonPropertyName("properties")]
        public ItemProperties Properties { get; set; }
    
        [JsonIgnore]
        public string Caliber => Properties?.Caliber;
    
        [JsonIgnore]
        public long HighestVendorPrice => SellFor?
            .Where(x => x.Vendor.Name != "Flea Market" && x.PriceRub is long)?
            .Select(x => x.PriceRub)?
            .DefaultIfEmpty()?
            .Max() ?? 0;
    
        [JsonIgnore]
        public long OptimalFleaPrice
        {
            get
            {
                if (BasePrice == 0)
                    return 0;
                if (Avg24HPrice is long avg24 && FleaTax.Calculate(avg24, BasePrice) < avg24)
                    return avg24;
                return (long)(HistoricalPrices?
                    .Where(x => x.Price is long price && FleaTax.Calculate(price, BasePrice) < price)?
                    .Select(x => x.Price)?
                    .DefaultIfEmpty()?
                    .Average() ?? 0);
            }
        }
    
        public partial class HistoricalPrice
        {
            [JsonPropertyName("price")]
            public long? Price { get; set; }
        }
    
        public partial class SellForElement
        {
            [JsonPropertyName("priceRUB")]
            public long? PriceRub { get; set; }
    
            [JsonPropertyName("vendor")]
            public CategoryElement Vendor { get; set; }
        }
    
        public partial class CategoryElement
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }
        }
    
        public partial class ItemProperties
        {
            [JsonPropertyName("caliber")]
            public string Caliber { get; set; }
        }
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

    public partial class QuestItemElement
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("shortName")]
        public string ShortName { get; set; }
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
        public List<ObjectiveElement> Objectives { get; set; }

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
}
