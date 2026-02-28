using System.Text.Json.Serialization;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites.Chams
{
    public enum ChamsMode : int
    {
        Basic = 1,
        VisCheckGlow = 2,
        Visible = 3,
        VisCheckFlat = 4,
        WireFrame = 5,
        Aimbot = 6
    }

    public enum ChamsEntityType
    {
        PMC,
        Teammate,
        AI,
        AimbotTarget,
        Streamer,
        Boss,
        Guard,
        PlayerScav,
        Container,
        QuestItem,
        ImportantItem,
        Grenade,
        All
    }

    public sealed class ChamsMaterial
    {
        public ulong Address { get; init; }
        public int InstanceID { get; init; }
        public int ColorVisible { get; set; }
        public int ColorInvisible { get; set; }
    }

    public class ChamsMaterialStatus
    {
        public int ExpectedCount { get; set; }
        public int LoadedCount { get; set; }
        public int WorkingCount { get; set; }
        public int FailedCount { get; set; }
        public List<(ChamsMode, ChamsEntityType)> MissingCombos { get; set; } = new();
        public List<(ChamsMode, ChamsEntityType)> FailedCombos { get; set; } = new();

        public bool IsComplete => LoadedCount == ExpectedCount && WorkingCount == ExpectedCount;
        public bool IsPartial => LoadedCount > 0 && LoadedCount < ExpectedCount;
        public string StatusText => IsComplete ? "Complete" : IsPartial ? "Partial" : "Failed";
    }
}