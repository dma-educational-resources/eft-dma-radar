namespace eft_dma_radar.Silk.DMA;

/// <summary>
/// Manages hotkey bindings between <see cref="SilkConfig"/> key codes and
/// <see cref="InputManager"/> actions. Handles registration, re-binding,
/// and cleanup.
/// </summary>
internal static class HotkeyManager
{
    /// <summary>All known hotkey actions that can be bound.</summary>
    internal static readonly HotkeyAction[] Actions =
    [
        new("BattleMode", "Battle Mode", "Toggle battle mode (hide loot, focus players)",
            static () => SilkProgram.Config.HotkeyBattleMode,
            static v => SilkProgram.Config.HotkeyBattleMode = v,
            static e => { if (e.IsDown) SilkProgram.Config.BattleMode = !SilkProgram.Config.BattleMode; }),

        new("FreeMode", "Free Mode", "Toggle between player-follow and free-pan",
            static () => SilkProgram.Config.HotkeyFreeMode,
            static v => SilkProgram.Config.HotkeyFreeMode = v,
            static e => { if (e.IsDown) RadarWindow.FreeMode = !RadarWindow.FreeMode; }),

        new("ToggleLoot", "Toggle Loot", "Toggle loot overlay visibility",
            static () => SilkProgram.Config.HotkeyToggleLoot,
            static v => SilkProgram.Config.HotkeyToggleLoot = v,
            static e => { if (e.IsDown) SilkProgram.Config.ShowLoot = !SilkProgram.Config.ShowLoot; }),

        new("ZoomIn", "Zoom In", "Zoom in on the radar map",
            static () => SilkProgram.Config.HotkeyZoomIn,
            static v => SilkProgram.Config.HotkeyZoomIn = v,
            static e => { if (e.IsDown) RadarWindow.Zoom = Math.Min(RadarWindow.Zoom + 5, 200); }),

        new("ZoomOut", "Zoom Out", "Zoom out on the radar map",
            static () => SilkProgram.Config.HotkeyZoomOut,
            static v => SilkProgram.Config.HotkeyZoomOut = v,
            static e => { if (e.IsDown) RadarWindow.Zoom = Math.Max(RadarWindow.Zoom - 5, 1); }),
    ];

    /// <summary>
    /// The action currently being rebound (waiting for a key press), or <c>null</c>.
    /// </summary>
    internal static HotkeyAction? RebindingAction { get; set; }

    /// <summary>
    /// Registers all hotkey actions with <see cref="InputManager"/>.
    /// Call after <see cref="InputManager.Initialize"/> succeeds.
    /// Safe to call multiple times — re-registers with current config values.
    /// </summary>
    public static void RegisterAll()
    {
        if (!InputManager.IsReady)
            return;

        foreach (var action in Actions)
        {
            int vk = action.GetKeyCode();
            if (vk > 0)
            {
                InputManager.RegisterKeyAction(vk, action.Id, action.Handler);
                Log.WriteLine($"[HotkeyManager] Registered '{action.DisplayName}' on {VK.GetName(vk)}");
            }
        }
    }

    /// <summary>
    /// Re-binds a single hotkey action to a new key. Unregisters the old key first.
    /// </summary>
    public static void Rebind(HotkeyAction action, int newVk)
    {
        int oldVk = action.GetKeyCode();
        if (oldVk > 0)
            InputManager.UnregisterKeyAction(oldVk, action.Id);

        action.SetKeyCode(newVk);

        if (newVk > 0 && InputManager.IsReady)
            InputManager.RegisterKeyAction(newVk, action.Id, action.Handler);

        SilkProgram.Config.MarkDirty();
    }

    /// <summary>
    /// Clears the binding for a hotkey action (sets to 0 / None).
    /// </summary>
    public static void ClearBinding(HotkeyAction action)
    {
        Rebind(action, 0);
    }

    /// <summary>
    /// Unregisters all hotkey actions.
    /// </summary>
    public static void UnregisterAll()
    {
        foreach (var action in Actions)
        {
            int vk = action.GetKeyCode();
            if (vk > 0)
                InputManager.UnregisterKeyAction(vk, action.Id);
        }
    }

    /// <summary>
    /// Called from the render loop to check if a rebind capture is active.
    /// If the user pressed any key, completes the rebind.
    /// Returns <c>true</c> if a rebind was captured this frame.
    /// </summary>
    public static bool TryCaptureRebind()
    {
        if (RebindingAction is null || !InputManager.IsReady)
            return false;

        // Scan all VKs (1..254) for a new key press
        for (int vk = 1; vk < 255; vk++)
        {
            // Skip mouse buttons — not useful as hotkeys
            if (vk is VK.LBUTTON or VK.RBUTTON or VK.MBUTTON or VK.XBUTTON1 or VK.XBUTTON2)
                continue;

            if (InputManager.IsKeyPressed(vk))
            {
                // Escape cancels the rebind
                if (vk == VK.ESCAPE)
                {
                    RebindingAction = null;
                    return true;
                }

                Rebind(RebindingAction, vk);
                Log.WriteLine($"[HotkeyManager] Rebound '{RebindingAction.DisplayName}' to {VK.GetName(vk)}");
                RebindingAction = null;
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Describes a hotkey action: unique ID, display name, tooltip, config getter/setter, and handler.
/// </summary>
internal sealed class HotkeyAction(
    string id,
    string displayName,
    string tooltip,
    Func<int> getKeyCode,
    Action<int> setKeyCode,
    Action<InputManager.KeyInputEventArgs> handler)
{
    /// <summary>Unique action identifier (used as registration name).</summary>
    public string Id { get; } = id;

    /// <summary>Human-readable name shown in UI.</summary>
    public string DisplayName { get; } = displayName;

    /// <summary>Tooltip description for the settings panel.</summary>
    public string Tooltip { get; } = tooltip;

    /// <summary>The handler invoked when the key state changes.</summary>
    public Action<InputManager.KeyInputEventArgs> Handler { get; } = handler;

    /// <summary>Gets the current virtual key code from config.</summary>
    public int GetKeyCode() => getKeyCode();

    /// <summary>Sets the virtual key code in config.</summary>
    public void SetKeyCode(int vk) => setKeyCode(vk);
}
