namespace eft_dma_radar.Silk.Misc.Input;

/// <summary>
/// Describes one possible hotkey action: unique ID, display name, category, and handler delegate.
/// The catalog of all available actions is defined in <see cref="HotkeyManager.AvailableActions"/>.
/// </summary>
internal sealed class HotkeyActionDef(
    string id,
    string displayName,
    string category,
    string tooltip,
    Action<InputManager.KeyInputEventArgs> handler)
{
    /// <summary>Unique action identifier, used as dictionary key in config.</summary>
    public string Id { get; } = id;

    /// <summary>Human-readable name shown in the UI.</summary>
    public string DisplayName { get; } = displayName;

    /// <summary>Category grouping for the UI (e.g. "General", "Loot").</summary>
    public string Category { get; } = category;

    /// <summary>Tooltip description for the settings panel.</summary>
    public string Tooltip { get; } = tooltip;

    /// <summary>The handler invoked when the key state changes.</summary>
    public Action<InputManager.KeyInputEventArgs> Handler { get; } = handler;
}

/// <summary>
/// Manages hotkey bindings between <see cref="SilkConfig.Hotkeys"/> entries and
/// <see cref="InputManager"/> actions. Supports dynamic add/remove and live rebinding.
/// </summary>
internal static class HotkeyManager
{
    /// <summary>
    /// Catalog of all available hotkey actions. Each entry defines what the action
    /// does — binding to a key is handled via the config dictionary.
    /// </summary>
    internal static readonly HotkeyActionDef[] AvailableActions =
    [
        // General
        new("BattleMode", "Battle Mode", "General",
            "Toggle battle mode (hide loot, focus players)",
            static e => { if (e.IsDown) SilkProgram.Config.BattleMode = !SilkProgram.Config.BattleMode; }),

        new("FreeMode", "Free Mode", "General",
            "Toggle between player-follow and free-pan",
            static e => { if (e.IsDown) RadarWindow.FreeMode = !RadarWindow.FreeMode; }),

        new("ZoomIn", "Zoom In", "General",
            "Zoom in on the radar map",
            static e => { if (e.IsDown) RadarWindow.Zoom = Math.Min(RadarWindow.Zoom + 5, 200); }),

        new("ZoomOut", "Zoom Out", "General",
            "Zoom out on the radar map",
            static e => { if (e.IsDown) RadarWindow.Zoom = Math.Max(RadarWindow.Zoom - 5, 1); }),

        // Loot
        new("ToggleLoot", "Toggle Loot", "Loot",
            "Toggle loot overlay visibility",
            static e => { if (e.IsDown) SilkProgram.Config.ShowLoot = !SilkProgram.Config.ShowLoot; }),

        new("ToggleContainers", "Toggle Containers", "Loot",
            "Toggle static container rendering on the radar",
            static e => { if (e.IsDown) SilkProgram.Config.ShowContainers = !SilkProgram.Config.ShowContainers; }),

        new("ToggleCorpses", "Toggle Corpses", "Loot",
            "Toggle corpse marker rendering on the radar",
            static e => { if (e.IsDown) SilkProgram.Config.ShowCorpses = !SilkProgram.Config.ShowCorpses; }),

        // Map
        new("ToggleExfils", "Toggle Exfils", "Map",
            "Toggle exfil point rendering on the radar",
            static e => { if (e.IsDown) SilkProgram.Config.ShowExfils = !SilkProgram.Config.ShowExfils; }),

        new("ToggleDoors", "Toggle Doors", "Map",
            "Toggle keyed door rendering on the radar",
            static e => { if (e.IsDown) SilkProgram.Config.ShowDoors = !SilkProgram.Config.ShowDoors; }),

        // Widgets
        new("ToggleAimview", "Toggle Aimview", "Widgets",
            "Show/hide the aimview widget",
            static e => { if (e.IsDown) SilkProgram.Config.ShowAimview = !SilkProgram.Config.ShowAimview; }),

        new("TogglePlayers", "Toggle Players On Top", "Widgets",
            "Toggle drawing players above all other entities",
            static e => { if (e.IsDown) SilkProgram.Config.PlayersOnTop = !SilkProgram.Config.PlayersOnTop; }),

        new("ConnectGroups", "Connect Groups", "Widgets",
            "Toggle squad connection lines",
            static e => { if (e.IsDown) SilkProgram.Config.ConnectGroups = !SilkProgram.Config.ConnectGroups; }),

        // ESP
        new("ToggleEspWindow", "Toggle ESP Window", "ESP",
            "Open/close the ESP overlay window",
            static e => { if (e.IsDown) eft_dma_radar.Silk.UI.ESP.EspWindow.Toggle(); }),

        new("EspCycleRenderMode", "Cycle ESP Render Mode", "ESP",
            "Cycle player render mode: None → Bones → Box → HeadDot",
            static e => { if (e.IsDown) eft_dma_radar.Silk.UI.ESP.EspWindow.CycleRenderMode(); }),

        new("EspToggleCrosshair", "Toggle ESP Crosshair", "ESP",
            "Show/hide the center crosshair overlay on the ESP window",
            static e => { if (e.IsDown) { SilkProgram.Config.EspShowCrosshair = !SilkProgram.Config.EspShowCrosshair; SilkProgram.Config.MarkDirty(); } }),
    ];

    /// <summary>Lookup from action ID to definition, for fast access.</summary>
    private static readonly Dictionary<string, HotkeyActionDef> _actionLookup;

    /// <summary>
    /// The action ID currently being captured for key binding, or <c>null</c>.
    /// </summary>
    internal static string? CapturingActionId { get; set; }

    static HotkeyManager()
    {
        _actionLookup = new(AvailableActions.Length, StringComparer.Ordinal);
        foreach (var a in AvailableActions)
            _actionLookup[a.Id] = a;
    }

    /// <summary>
    /// Gets the action definition by ID, or <c>null</c> if not found.
    /// </summary>
    public static HotkeyActionDef? GetAction(string id)
        => _actionLookup.GetValueOrDefault(id);

    /// <summary>
    /// Registers all enabled hotkeys from config with <see cref="InputManager"/>.
    /// Safe to call multiple times — re-registers with current config values.
    /// </summary>
    public static void RegisterAll()
    {
        if (!InputManager.IsReady)
            return;

        var hotkeys = SilkProgram.Config.Hotkeys;
        foreach (var (id, entry) in hotkeys)
        {
            if (!entry.Enabled || entry.Key < 1)
                continue;

            if (_actionLookup.TryGetValue(id, out var def))
            {
                InputManager.RegisterKeyAction(entry.Key, id, def.Handler);
                Log.WriteLine($"[HotkeyManager] Registered '{def.DisplayName}' on {VK.GetName(entry.Key)}");
            }
        }
    }

    /// <summary>
    /// Unregisters all hotkeys from <see cref="InputManager"/>.
    /// </summary>
    public static void UnregisterAll()
    {
        var hotkeys = SilkProgram.Config.Hotkeys;
        foreach (var (id, entry) in hotkeys)
        {
            if (entry.Key > 0)
                InputManager.UnregisterKeyAction(entry.Key, id);
        }
    }

    /// <summary>
    /// Adds or updates a hotkey binding for the given action.
    /// Unregisters any previous key and registers the new one.
    /// </summary>
    public static void AddOrUpdate(string actionId, int vk, HotkeyMode mode)
    {
        var config = SilkProgram.Config;
        var hotkeys = config.Hotkeys;

        // Unregister old binding if present
        if (hotkeys.TryGetValue(actionId, out var existing) && existing.Key > 0)
            InputManager.UnregisterKeyAction(existing.Key, actionId);

        hotkeys[actionId] = new HotkeyEntry
        {
            Enabled = true,
            Key = vk,
            Mode = mode,
        };

        // Register new binding
        if (vk > 0 && InputManager.IsReady && _actionLookup.TryGetValue(actionId, out var def))
            InputManager.RegisterKeyAction(vk, actionId, def.Handler);

        config.MarkDirty();
    }

    /// <summary>
    /// Removes a hotkey binding for the given action.
    /// </summary>
    public static void Remove(string actionId)
    {
        var config = SilkProgram.Config;
        var hotkeys = config.Hotkeys;

        if (hotkeys.TryGetValue(actionId, out var entry))
        {
            if (entry.Key > 0)
                InputManager.UnregisterKeyAction(entry.Key, actionId);

            hotkeys.Remove(actionId);
            config.MarkDirty();
        }
    }

    /// <summary>
    /// Called from the render loop to check if a key capture is active.
    /// If the user pressed any key, completes the capture.
    /// Returns <c>true</c> if a capture was completed this frame.
    /// </summary>
    public static bool TryCaptureKey(out int capturedVk)
    {
        capturedVk = -1;

        if (CapturingActionId is null || !InputManager.IsReady)
            return false;

        for (int vk = 1; vk < 255; vk++)
        {
            if (vk is VK.LBUTTON or VK.RBUTTON or VK.MBUTTON or VK.XBUTTON1 or VK.XBUTTON2)
                continue;

            if (InputManager.IsKeyPressed(vk))
            {
                if (vk == VK.ESCAPE)
                {
                    CapturingActionId = null;
                    return true;
                }

                capturedVk = vk;
                CapturingActionId = null;
                return true;
            }
        }

        return false;
    }
}
