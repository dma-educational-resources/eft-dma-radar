using eft_dma_radar.Misc.Config;

namespace eft_dma_radar.Misc.Config
{
    public enum HotkeyMode
    {
        Toggle = 0,
        OnKey = 1
    }

    public class HotkeyActionModel
    {
        public string Name { get; set; }  // Display name like "Toggle ESP"
        public string Key { get; set; }   // Internal config key like "ToggleESP"
    }

    /// <summary>
    /// Individual hotkey entry for each action.
    /// </summary>
    public sealed class HotkeyEntry
    {
        /// <summary>
        /// If the hotkey is enabled.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Hotkey trigger mode: Toggle or OnKey (Hold).
        /// </summary>
        [JsonPropertyName("mode")]
        public HotkeyMode Mode { get; set; } = HotkeyMode.Toggle;

        /// <summary>
        /// Virtual keycode (int) for the hotkey.
        /// </summary>
        [JsonPropertyName("key")]
        public int Key { get; set; } = -1;
    }
}
