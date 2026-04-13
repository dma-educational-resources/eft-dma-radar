using VmmSharpEx;
using VmmSharpEx.Extensions.Input;

namespace eft_dma_radar.Silk.DMA;

/// <summary>
/// DMA-based keyboard/mouse input manager using <see cref="VmmInputManager"/>.
/// Polls key state from the target machine's kernel via DMA at ~100 Hz.
/// <para>
/// Supports Windows 10 and Windows 11 (all builds) via VmmSharpEx's
/// automatic Win10/Win11 resolution with fallback.
/// </para>
/// </summary>
internal static class InputManager
{
    #region Constants

    /// <summary>Polling interval for the input worker thread (milliseconds).</summary>
    private const int PollIntervalMs = 10;

    /// <summary>Max initialization attempts before entering safe mode.</summary>
    private const int MaxInitAttempts = 3;

    /// <summary>Double-tap detection window (milliseconds).</summary>
    private const int DoubleTapThresholdMs = 300;

    #endregion

    #region Fields

    private static VmmInputManager? _vmmInput;
    private static volatile bool _initialized;
    private static volatile bool _safeMode;
    private static volatile bool _shutdown;
    private static Thread? _workerThread;
    private static CancellationTokenSource? _cts;

    /// <summary>Current frame's raw key state bitmap (64 bytes = 256 virtual keys × 2 bits each).</summary>
    private static readonly byte[] _currentState = new byte[64];
    /// <summary>Previous frame's raw key state bitmap for edge detection.</summary>
    private static readonly byte[] _previousState = new byte[64];
    /// <summary>Set of currently-pressed virtual key codes (for fast IsKeyDown lookups).</summary>
    private static readonly HashSet<int> _pressedKeys = [];

    /// <summary>Registered key action handlers keyed by virtual key code.</summary>
    private static readonly Dictionary<int, List<KeyActionEntry>> _handlers = [];
    private static readonly Lock _handlerLock = new();
    private static int _nextActionId = 1;

    /// <summary>Double-tap toggle state tracking.</summary>
    private static readonly Dictionary<int, long> _lastTapTicks = [];
    private static readonly Dictionary<int, bool> _toggleStates = [];

    #endregion

    #region Public API — Lifecycle

    /// <summary>Whether the input manager is ready to poll keys.</summary>
    public static bool IsReady => _initialized && !_safeMode;

    /// <summary>
    /// Initializes the input manager. Must be called after <see cref="Memory.ModuleInit"/>.
    /// Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized || _safeMode)
            return;

        var vmm = Memory.VmmHandle;
        if (vmm is null)
        {
            _safeMode = true;
            Log.WriteLine("[InputManager] VMM not available — safe mode (input disabled).");
            return;
        }

        int attempts = 0;
        while (attempts < MaxInitAttempts)
        {
            try
            {
                _vmmInput = new VmmInputManager(vmm, msg => Log.WriteLine($"[InputManager] {msg}"));
                _initialized = true;
                Log.WriteLine($"[InputManager] Initialized OK — build {_vmmInput.TargetBuildNumber}, method: {_vmmInput.ResolutionMethod}");
                break;
            }
            catch (VmmException ex)
            {
                attempts++;
                Log.WriteLine($"[InputManager] Init attempt {attempts}/{MaxInitAttempts} failed: {ex.Message}");
                if (attempts < MaxInitAttempts)
                    Thread.Sleep(500);
            }
        }

        if (!_initialized)
        {
            _safeMode = true;
            Log.WriteLine("[InputManager] All init attempts failed — safe mode (input disabled). Restart gaming PC if hotkeys are needed.");
            return;
        }

        // Start the polling worker thread
        _cts = new CancellationTokenSource();
        _workerThread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "InputManager",
            Priority = ThreadPriority.AboveNormal
        };
        _workerThread.Start();
    }

    /// <summary>
    /// Shuts down the input manager. Called from <see cref="Memory.Close"/> or program exit.
    /// </summary>
    public static void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;

        try { _cts?.Cancel(); } catch { }
        _workerThread?.Join(TimeSpan.FromSeconds(2));
        _workerThread = null;
        _vmmInput = null;
        _initialized = false;

        Log.WriteLine("[InputManager] Shut down.");
    }

    #endregion

    #region Public API — Key Queries

    /// <summary>
    /// Returns <see langword="true"/> if the given virtual key is currently held down.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsKeyDown(int vk)
    {
        if (!_initialized || _safeMode)
            return false;
        return _pressedKeys.Contains(vk);
    }

    /// <summary>
    /// Returns <see langword="true"/> if the given virtual key is currently held down.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsKeyDown(Win32VirtualKey vk) => IsKeyDown((int)vk);

    /// <summary>
    /// Returns <see langword="true"/> if the key was just pressed this frame (rising edge).
    /// </summary>
    public static bool IsKeyPressed(int vk)
    {
        if (!_initialized || _safeMode)
            return false;

        // Currently down AND was NOT down last frame
        if (!_pressedKeys.Contains(vk))
            return false;

        int byteIdx = vk * 2 / 8;
        int bitMask = 1 << (vk % 4 * 2);
        return (_previousState[byteIdx] & bitMask) == 0;
    }

    /// <summary>
    /// Returns <see langword="true"/> if the given virtual key is currently held down.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsKeyPressed(Win32VirtualKey vk) => IsKeyPressed((int)vk);

    /// <summary>
    /// Double-tap toggle: returns <see langword="true"/> after two quick presses, stays true until toggled off.
    /// Useful for "hold" modes toggled via double-tap.
    /// </summary>
    public static bool IsKeyHeldToggle(int vk)
    {
        if (!_initialized || _safeMode)
            return false;

        if (!IsKeyPressed(vk))
            return _toggleStates.TryGetValue(vk, out var held) && held;

        long nowTicks = Stopwatch.GetTimestamp();
        long thresholdTicks = (long)(DoubleTapThresholdMs / 1000.0 * Stopwatch.Frequency);

        lock (_handlerLock)
        {
            if (_lastTapTicks.TryGetValue(vk, out long lastTap))
            {
                if (nowTicks - lastTap < thresholdTicks)
                {
                    _toggleStates[vk] = !_toggleStates.GetValueOrDefault(vk, false);
                    _lastTapTicks.Remove(vk);
                }
                else
                {
                    _lastTapTicks[vk] = nowTicks;
                }
            }
            else
            {
                _lastTapTicks[vk] = nowTicks;
            }
        }

        return _toggleStates.TryGetValue(vk, out var isHeld) && isHeld;
    }

    /// <inheritdoc cref="IsKeyHeldToggle(int)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsKeyHeldToggle(Win32VirtualKey vk) => IsKeyHeldToggle((int)vk);

    #endregion

    #region Public API — Action Registration

    /// <summary>
    /// Registers a named action for a virtual key code. Returns an action ID for later removal.
    /// If an action with the same name already exists on this key, the handler is replaced.
    /// </summary>
    /// <returns>Action ID (&gt; 0), or -1 if registration failed.</returns>
    public static int RegisterKeyAction(int vk, string actionName, Action<KeyInputEventArgs> handler)
    {
        if (!IsReady || handler is null || string.IsNullOrEmpty(actionName))
            return -1;

        lock (_handlerLock)
        {
            if (!_handlers.TryGetValue(vk, out var list))
            {
                list = [];
                _handlers[vk] = list;
            }

            // Replace existing action with same name
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].ActionName == actionName)
                {
                    list[i] = list[i] with { Handler = handler };
                    return list[i].ActionId;
                }
            }

            var id = _nextActionId++;
            list.Add(new KeyActionEntry(id, actionName, handler));
            return id;
        }
    }

    /// <inheritdoc cref="RegisterKeyAction(int, string, Action{KeyInputEventArgs})"/>
    public static int RegisterKeyAction(Win32VirtualKey vk, string actionName, Action<KeyInputEventArgs> handler)
        => RegisterKeyAction((int)vk, actionName, handler);

    /// <summary>Unregisters a key action by name.</summary>
    public static bool UnregisterKeyAction(int vk, string actionName)
    {
        lock (_handlerLock)
        {
            if (!_handlers.TryGetValue(vk, out var list))
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].ActionName == actionName)
                {
                    list.RemoveAt(i);
                    if (list.Count == 0)
                        _handlers.Remove(vk);
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>Unregisters a key action by its action ID.</summary>
    public static bool UnregisterKeyAction(int actionId)
    {
        lock (_handlerLock)
        {
            foreach (var (vk, list) in _handlers)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].ActionId == actionId)
                    {
                        list.RemoveAt(i);
                        if (list.Count == 0)
                            _handlers.Remove(vk);
                        return true;
                    }
                }
            }
            return false;
        }
    }

    /// <summary>Removes all registered actions for a specific key.</summary>
    public static void ClearKeyActions(int vk)
    {
        lock (_handlerLock)
            _handlers.Remove(vk);
    }

    /// <summary>Returns all registered action names for a key.</summary>
    public static string[] GetKeyActions(int vk)
    {
        lock (_handlerLock)
        {
            if (!_handlers.TryGetValue(vk, out var list) || list.Count == 0)
                return [];

            var names = new string[list.Count];
            for (int i = 0; i < list.Count; i++)
                names[i] = list[i].ActionName;
            return names;
        }
    }

    #endregion

    #region Worker Thread

    private static void WorkerLoop()
    {
        Log.WriteLine("[InputManager] Worker thread started.");
        var ct = _cts!.Token;

        while (!ct.IsCancellationRequested && !_shutdown)
        {
            try
            {
                PollAndDispatch();
            }
            catch (ObjectDisposedException)
            {
                // VMM handle disposed — shutdown in progress
                break;
            }
            catch (VmmException)
            {
                // DMA read failed — transient; skip this frame
            }
            catch (Exception ex) when (!_shutdown)
            {
                Log.WriteLine($"[InputManager] Worker error: {ex.Message}");
            }
            catch { break; }

            try { Thread.Sleep(PollIntervalMs); }
            catch (ThreadInterruptedException) { break; }
        }

        Log.WriteLine("[InputManager] Worker thread exiting.");
    }

    /// <summary>
    /// Single tick: snapshot previous state, poll new state, detect edges, dispatch handlers.
    /// </summary>
    private static void PollAndDispatch()
    {
        var input = _vmmInput;
        if (input is null)
            return;

        // Snapshot previous state
        Array.Copy(_currentState, _previousState, 64);

        // Read new key state from DMA
        input.UpdateKeys();

        // Rebuild current state + pressed set
        _pressedKeys.Clear();

        for (int vk = 0; vk < 256; vk++)
        {
            bool isDown = input.IsKeyDown((uint)vk);

            // Sync our bitmap so _previousState is correct next frame
            int byteIdx = vk * 2 / 8;
            int bitMask = 1 << (vk % 4 * 2);
            if (isDown)
                _currentState[byteIdx] |= (byte)bitMask;
            else
                _currentState[byteIdx] &= (byte)~bitMask;

            if (isDown)
                _pressedKeys.Add(vk);

            // Edge detection — only fire handlers on transitions
            bool wasDown = (_previousState[byteIdx] & bitMask) != 0;
            if (wasDown == isDown)
                continue;

            // Take snapshot of handlers under lock, dispatch outside lock
            KeyActionEntry[]? snapshot;
            lock (_handlerLock)
            {
                if (!_handlers.TryGetValue(vk, out var list) || list.Count == 0)
                    continue;
                snapshot = list.ToArray();
            }

            var args = new KeyInputEventArgs(vk, isDown);
            for (int i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i].Handler(args);
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[InputManager] Handler '{snapshot[i].ActionName}' error: {ex.Message}");
                }
            }
        }
    }

    #endregion

    #region Nested Types

    /// <summary>A registered key action binding.</summary>
    private sealed record KeyActionEntry(int ActionId, string ActionName, Action<KeyInputEventArgs> Handler);

    /// <summary>Event args for key state change events.</summary>
    public readonly struct KeyInputEventArgs(int keyCode, bool isDown)
    {
        /// <summary>The Win32 virtual key code.</summary>
        public int KeyCode { get; } = keyCode;

        /// <summary><see langword="true"/> if the key was just pressed; <see langword="false"/> if released.</summary>
        public bool IsDown { get; } = isDown;

        /// <summary><see langword="true"/> if the key was just released.</summary>
        public bool IsUp => !IsDown;
    }

    #endregion
}

/// <summary>
/// Common virtual key codes as constants for use without referencing <see cref="Win32VirtualKey"/>.
/// These correspond to Win32 VK_* codes.
/// </summary>
/// <remarks>
/// See: <see href="https://learn.microsoft.com/windows/win32/inputdev/virtual-key-codes"/>
/// </remarks>
internal static class VK
{
    // Mouse
    public const int LBUTTON = 0x01;
    public const int RBUTTON = 0x02;
    public const int MBUTTON = 0x04;
    public const int XBUTTON1 = 0x05;
    public const int XBUTTON2 = 0x06;

    // Common
    public const int BACK = 0x08;
    public const int TAB = 0x09;
    public const int RETURN = 0x0D;
    public const int SHIFT = 0x10;
    public const int CONTROL = 0x11;
    public const int MENU = 0x12; // Alt
    public const int PAUSE = 0x13;
    public const int CAPITAL = 0x14; // Caps Lock
    public const int ESCAPE = 0x1B;
    public const int SPACE = 0x20;
    public const int PRIOR = 0x21; // Page Up
    public const int NEXT = 0x22;  // Page Down
    public const int END = 0x23;
    public const int HOME = 0x24;
    public const int LEFT = 0x25;
    public const int UP = 0x26;
    public const int RIGHT = 0x27;
    public const int DOWN = 0x28;
    public const int SNAPSHOT = 0x2C; // Print Screen
    public const int INSERT = 0x2D;
    public const int DELETE = 0x2E;

    // Numbers
    public const int D0 = 0x30;
    public const int D1 = 0x31;
    public const int D2 = 0x32;
    public const int D3 = 0x33;
    public const int D4 = 0x34;
    public const int D5 = 0x35;
    public const int D6 = 0x36;
    public const int D7 = 0x37;
    public const int D8 = 0x38;
    public const int D9 = 0x39;

    // Letters
    public const int A = 0x41;
    public const int B = 0x42;
    public const int C = 0x43;
    public const int D = 0x44;
    public const int E = 0x45;
    public const int F = 0x46;
    public const int G = 0x47;
    public const int H = 0x48;
    public const int I = 0x49;
    public const int J = 0x4A;
    public const int K = 0x4B;
    public const int L = 0x4C;
    public const int M = 0x4D;
    public const int N = 0x4E;
    public const int O = 0x4F;
    public const int P = 0x50;
    public const int Q = 0x51;
    public const int R = 0x52;
    public const int S = 0x53;
    public const int T = 0x54;
    public const int U = 0x55;
    public const int V = 0x56;
    public const int W = 0x57;
    public const int X = 0x58;
    public const int Y = 0x59;
    public const int Z = 0x5A;

    // Function keys
    public const int F1 = 0x70;
    public const int F2 = 0x71;
    public const int F3 = 0x72;
    public const int F4 = 0x73;
    public const int F5 = 0x74;
    public const int F6 = 0x75;
    public const int F7 = 0x76;
    public const int F8 = 0x77;
    public const int F9 = 0x78;
    public const int F10 = 0x79;
    public const int F11 = 0x7A;
    public const int F12 = 0x7B;

    // Numpad
    public const int NUMPAD0 = 0x60;
    public const int NUMPAD1 = 0x61;
    public const int NUMPAD2 = 0x62;
    public const int NUMPAD3 = 0x63;
    public const int NUMPAD4 = 0x64;
    public const int NUMPAD5 = 0x65;
    public const int NUMPAD6 = 0x66;
    public const int NUMPAD7 = 0x67;
    public const int NUMPAD8 = 0x68;
    public const int NUMPAD9 = 0x69;
    public const int MULTIPLY = 0x6A;
    public const int ADD = 0x6B;
    public const int SUBTRACT = 0x6D;
    public const int DECIMAL = 0x6E;
    public const int DIVIDE = 0x6F;

    // Modifiers
    public const int LSHIFT = 0xA0;
    public const int RSHIFT = 0xA1;
    public const int LCONTROL = 0xA2;
    public const int RCONTROL = 0xA3;
    public const int LMENU = 0xA4; // Left Alt
    public const int RMENU = 0xA5; // Right Alt

    /// <summary>
    /// Gets a human-readable display name for a virtual key code.
    /// </summary>
    public static string GetName(int vk) => vk switch
    {
        LBUTTON => "LMB",
        RBUTTON => "RMB",
        MBUTTON => "MMB",
        XBUTTON1 => "Mouse4",
        XBUTTON2 => "Mouse5",
        BACK => "Backspace",
        TAB => "Tab",
        RETURN => "Enter",
        SHIFT => "Shift",
        CONTROL => "Ctrl",
        MENU => "Alt",
        PAUSE => "Pause",
        CAPITAL => "CapsLock",
        ESCAPE => "Esc",
        SPACE => "Space",
        PRIOR => "PageUp",
        NEXT => "PageDown",
        END => "End",
        HOME => "Home",
        LEFT => "Left",
        UP => "Up",
        RIGHT => "Right",
        DOWN => "Down",
        SNAPSHOT => "PrintScr",
        INSERT => "Insert",
        DELETE => "Delete",
        >= D0 and <= D9 => ((char)vk).ToString(),
        >= A and <= Z => ((char)vk).ToString(),
        >= F1 and <= F12 => $"F{vk - F1 + 1}",
        >= NUMPAD0 and <= NUMPAD9 => $"Num{vk - NUMPAD0}",
        MULTIPLY => "Num*",
        ADD => "Num+",
        SUBTRACT => "Num-",
        DECIMAL => "Num.",
        DIVIDE => "Num/",
        LSHIFT => "LShift",
        RSHIFT => "RShift",
        LCONTROL => "LCtrl",
        RCONTROL => "RCtrl",
        LMENU => "LAlt",
        RMENU => "RAlt",
        _ => $"0x{vk:X2}"
    };
}
