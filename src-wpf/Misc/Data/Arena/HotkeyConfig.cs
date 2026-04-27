using eft_dma_radar.Misc.Config;

namespace eft_dma_radar.Misc.Data.Arena
{
    /// <summary>
    /// Wrapper for all configurable hotkey actions.
    /// </summary>
    public sealed class HotkeyConfig
    {
        #region Loot
        [JsonPropertyName("showThrowables")] public HotkeyEntry ShowThrowables { get; set; } = new();
        [JsonPropertyName("showWeapons")] public HotkeyEntry ShowWeapons { get; set; } = new();
        [JsonPropertyName("showMeds")] public HotkeyEntry ShowMeds { get; set; } = new();
        [JsonPropertyName("showBackpacks")] public HotkeyEntry ShowBackpacks { get; set; } = new();
        #endregion

        #region Fuser ESP
        [JsonPropertyName("fuserESP")] public HotkeyEntry ToggleFuserESP { get; set; } = new();
        [JsonPropertyName("espRadarZoomIn")] public HotkeyEntry MiniRadarZoomIn { get; set; } = new();
        [JsonPropertyName("espRadarZoomOut")] public HotkeyEntry MiniRadarZoomOut { get; set; } = new();
        #endregion

        #region Memory Writes
        // Global
        [JsonPropertyName("rageMode")] public HotkeyEntry ToggleRageMode { get; set; } = new();

        // Aimbot
        [JsonPropertyName("aimbot")] public HotkeyEntry ToggleAimbot { get; set; } = new();
        [JsonPropertyName("engageAimbot")] public HotkeyEntry EngageAimbot { get; set; } = new();
        [JsonPropertyName("aimbotMode")] public HotkeyEntry ToggleAimbotMode { get; set; } = new();
        [JsonPropertyName("aimbotBone")] public HotkeyEntry AimbotBone { get; set; } = new();
        [JsonPropertyName("safeLock")] public HotkeyEntry SafeLock { get; set; } = new();
        [JsonPropertyName("randomBone")] public HotkeyEntry RandomBone { get; set; } = new();
        [JsonPropertyName("autoBone")] public HotkeyEntry AutoBone { get; set; } = new();
        [JsonPropertyName("headshotAI")] public HotkeyEntry HeadshotAI { get; set; } = new();

        // Weapons
        [JsonPropertyName("noMalfunctions")] public HotkeyEntry NoMalfunctions { get; set; } = new();
        [JsonPropertyName("fastWeaponOps")] public HotkeyEntry FastWeaponOps { get; set; } = new();
        [JsonPropertyName("disableWeaponCollision")] public HotkeyEntry DisableWeaponCollision { get; set; } = new();
        [JsonPropertyName("noRecoil")] public HotkeyEntry NoRecoil { get; set; } = new();

        // Movement
        [JsonPropertyName("wideLean")] public HotkeyEntry WideLean { get; set; } = new();
        [JsonPropertyName("wideLeanUp")] public HotkeyEntry WideLeanUp { get; set; } = new();
        [JsonPropertyName("wideLeanRight")] public HotkeyEntry WideLeanRight { get; set; } = new();
        [JsonPropertyName("wideLeanLeft")] public HotkeyEntry WideLeanLeft { get; set; } = new();
        [JsonPropertyName("moveSpeed")] public HotkeyEntry MoveSpeed { get; set; } = new();

        // World
        [JsonPropertyName("disableShadows")] public HotkeyEntry DisableShadows { get; set; } = new();
        [JsonPropertyName("disableGrass")] public HotkeyEntry DisableGrass { get; set; } = new();
        [JsonPropertyName("clearWeather")] public HotkeyEntry ClearWeather { get; set; } = new();
        [JsonPropertyName("timeOfDay")] public HotkeyEntry TimeOfDay { get; set; } = new();
        [JsonPropertyName("fullBright")] public HotkeyEntry FullBright { get; set; } = new();

        // Camera
        [JsonPropertyName("noVisor")] public HotkeyEntry NoVisor { get; set; } = new();
        [JsonPropertyName("thermalVision")] public HotkeyEntry ThermalVision { get; set; } = new();
        [JsonPropertyName("thirdPerson")] public HotkeyEntry ThirdPerson { get; set; } = new();
        [JsonPropertyName("owlMode")] public HotkeyEntry OwlMode { get; set; } = new();
        [JsonPropertyName("instantZoom")] public HotkeyEntry InstantZoom { get; set; } = new();

        // Misc
        [JsonPropertyName("bigHeads")] public HotkeyEntry BigHeads { get; set; } = new();
        #endregion

        #region General Settings
        // General Options
        [JsonPropertyName("aimviewWidget")] public HotkeyEntry AimviewWidget { get; set; } = new();
        [JsonPropertyName("debugWidget")] public HotkeyEntry DebugWidget { get; set; } = new();
        [JsonPropertyName("playerInfoWidget")] public HotkeyEntry PlayerInfoWidget { get; set; } = new();
        [JsonPropertyName("connectGroups")] public HotkeyEntry ConnectGroups { get; set; } = new();
        [JsonPropertyName("maskNames")] public HotkeyEntry MaskNames { get; set; } = new();
        [JsonPropertyName("zoomOut")] public HotkeyEntry ZoomOut { get; set; } = new();
        [JsonPropertyName("zoomIn")] public HotkeyEntry ZoomIn { get; set; } = new();
        [JsonPropertyName("battleMode")] public HotkeyEntry BattleMode { get; set; } = new();
        #endregion
    }
}
