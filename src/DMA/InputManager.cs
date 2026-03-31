using eft_dma_radar.Common.Misc;
using VmmSharpEx;
using VmmSharpEx.Extensions;
using VmmSharpEx.Options;

namespace eft_dma_radar.DMA
{
    public static class InputManager
    {
        private static volatile bool _initialized = false;
        private static bool _safeMode = false;

        private static byte[] _currentStateBitmap = new byte[64];
        private static byte[] _previousStateBitmap = new byte[64];
        private static readonly HashSet<int> _pressedKeys = new();

        private static Vmm _hVMM;
        private static DmaInput _vmmInput;

        private static int _initAttempts = 0;
        private const int MAX_ATTEMPTS = 3;
        private const int DELAY = 500;
        private const int KEY_CHECK_DELAY = 100; // in milliseconds

        private static readonly Dictionary<int, DateTime> _lastKeyTapTime = new();
        private static readonly Dictionary<int, bool> _heldStates = new();
        private const int DoubleTapThresholdMs = 300;

        public static bool IsReady => _initialized;

        /// <summary>
        /// Raised once when InputManager transitions to the ready state.
        /// </summary>
#nullable enable
        public static event EventHandler? ReadyChanged;
#nullable restore

        private static readonly Dictionary<int, List<KeyActionHandler>> _keyActionHandlers = new();
        private static readonly object _eventLock = new();
        private static int _nextActionId = 1;

        /// <summary>
        /// Attempts to load Input Manager.
        /// </summary>
        public static void Initialize()
        {
            try
            {
                if (Memory.VmmHandle == null)
                {
                    _safeMode = true;
                    Log.WriteLine("[InputManager] Starting in Safe Mode - Input functionality disabled");
                    NotificationsShared.Warning("[InputManager] Safe Mode - Input functionality disabled");
                    return;
                }

                _hVMM = Memory.VmmHandle;

                if (_hVMM != null)
                {
                    new Thread(Worker)
                    {
                        IsBackground = true
                    }.Start();
                }

                if (InputManager.InitKeyboard())
                {
                    Log.WriteLine("[InputManager] Initialized");
                    NotificationsShared.Success("[InputManager] Initialized successfully!");
                }
                else
                {
                    Log.WriteLine("ERROR Initializing Input Manager");
                    NotificationsShared.Error("[InputManager] Failed to initialize, you may need to restart your gaming pc for hotkeys to work.");
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[InputManager] Error during initialization: {ex.Message}");
                _safeMode = true;
                NotificationsShared.Warning("[InputManager] Initialization failed - Safe Mode active");
            }
        }

        private static bool InitKeyboard()
        {
            if (_initialized)
                return true;

            if (_safeMode || _hVMM == null)
            {
                Log.WriteLine("[InputManager] Skipping keyboard initialization - Safe Mode");
                return false;
            }

            try
            {
                _vmmInput = new DmaInput(_hVMM);
                _initialized = true;
                ReadyChanged?.Invoke(null, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error initializing keyboard: {ex.Message}\n{ex.StackTrace}");
                _initAttempts++;
                return false;
            }
        }

        public static void UpdateKeys()
        {
            if (!_initialized || _safeMode || _vmmInput == null)
                return;

            Array.Copy(_currentStateBitmap, _previousStateBitmap, 64);

            _vmmInput.UpdateKeys();

            _pressedKeys.Clear();

            for (int vk = 0; vk < 256; ++vk)
            {
                var isDown = _vmmInput.IsKeyDown((uint)vk);

                // Keep _currentStateBitmap in sync with the live key state so that
                // _previousStateBitmap correctly reflects "was held last frame" next iteration.
                // Without this, _previousStateBitmap stays all-zeros permanently, which means
                // wasDown is always false: key-down events fire every frame while held, and
                // key-up events never fire.
                int byteIdx = vk * 2 / 8;
                int bitMask = 1 << (vk % 4 * 2);
                if (isDown)
                    _currentStateBitmap[byteIdx] |= (byte)bitMask;
                else
                    _currentStateBitmap[byteIdx] &= (byte)~bitMask;

                if (isDown)
                    _pressedKeys.Add(vk);

                var wasDown = (_previousStateBitmap[byteIdx] & bitMask) != 0;

                if (wasDown != isDown)
                {
                    KeyActionHandler[] snapshot;
                    lock (_eventLock)
                    {
                        if (!_keyActionHandlers.TryGetValue(vk, out var handlers))
                            continue;
                        snapshot = handlers.ToArray();
                    }

                    foreach (var handler in snapshot)
                    {
                        try
                        {
                            handler.Handler?.Invoke(null, new KeyEventArgs(vk, isDown));
                        }
                        catch (Exception ex)
                        {
                            Log.WriteLine($"Error executing key handler for action '{handler.ActionName}': {ex.Message}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Register a key action with a specific identifier. Returns the action ID for later removal.
        /// </summary>
        /// <param name="keyCode">The key code to listen for</param>
        /// <param name="actionName">Unique identifier for this action</param>
        /// <param name="handler">The handler to execute</param>
        /// <returns>Action ID for removal, or -1 if registration failed</returns>
        public static int RegisterKeyAction(int keyCode, string actionName, KeyStateChangedHandler handler)
        {
            if (!IsReady || _safeMode || handler == null || string.IsNullOrEmpty(actionName))
            {
                Log.WriteLine($"[InputManager] RegisterKeyAction skipped - Safe Mode or not ready");
                return -1;
            }

            lock (_eventLock)
            {
                if (!_keyActionHandlers.ContainsKey(keyCode))
                    _keyActionHandlers[keyCode] = new List<KeyActionHandler>();

                var existingAction = _keyActionHandlers[keyCode].FirstOrDefault(h => h.ActionName == actionName);
                if (existingAction != null)
                {
                    existingAction.Handler = handler;
                    return existingAction.ActionId;
                }

                var actionId = _nextActionId++;
                _keyActionHandlers[keyCode].Add(new KeyActionHandler
                {
                    ActionId = actionId,
                    ActionName = actionName,
                    Handler = handler
                });

                return actionId;
            }
        }

        /// <summary>
        /// Unregister a specific key action by action name
        /// </summary>
        /// <param name="keyCode">The key code</param>
        /// <param name="actionName">The action name to remove</param>
        /// <returns>True if the action was removed</returns>
        public static bool UnregisterKeyAction(int keyCode, string actionName)
        {
            if (_safeMode)
                return false;

            lock (_eventLock)
            {
                if (_keyActionHandlers.TryGetValue(keyCode, out var handlers))
                {
                    var removed = handlers.RemoveAll(h => h.ActionName == actionName) > 0;

                    if (handlers.Count == 0)
                        _keyActionHandlers.Remove(keyCode);

                    return removed;
                }
                return false;
            }
        }

        /// <summary>
        /// Unregister a specific key action by action ID
        /// </summary>
        /// <param name="actionId">The action ID to remove</param>
        /// <returns>True if the action was removed</returns>
        public static bool UnregisterKeyAction(int actionId)
        {
            if (_safeMode)
                return false;

            lock (_eventLock)
            {
                foreach (var kvp in _keyActionHandlers.ToList())
                {
                    var removed = kvp.Value.RemoveAll(h => h.ActionId == actionId) > 0;

                    if (kvp.Value.Count == 0)
                        _keyActionHandlers.Remove(kvp.Key);

                    if (removed)
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Remove all actions for a specific key
        /// </summary>
        /// <param name="keyCode">The key code to clear</param>
        public static void ClearKeyActions(int keyCode)
        {
            if (_safeMode)
                return;

            lock (_eventLock)
                _keyActionHandlers.Remove(keyCode);
        }

        /// <summary>
        /// Get all registered actions for a specific key
        /// </summary>
        /// <param name="keyCode">The key code</param>
        /// <returns>List of action names</returns>
        public static List<string> GetKeyActions(int keyCode)
        {
            if (_safeMode)
                return new List<string>();

            lock (_eventLock)
            {
                if (_keyActionHandlers.TryGetValue(keyCode, out var handlers))
                    return handlers.Select(h => h.ActionName).ToList();
                return new List<string>();
            }
        }

        /// <summary>
        /// Get all registered key-action pairs
        /// </summary>
        /// <returns>Dictionary of key codes to action names</returns>
        public static Dictionary<int, List<string>> GetAllKeyActions()
        {
            if (_safeMode)
                return new Dictionary<int, List<string>>();

            lock (_eventLock)
            {
                var result = new Dictionary<int, List<string>>();
                foreach (var kvp in _keyActionHandlers)
                {
                    result[kvp.Key] = kvp.Value.Select(h => h.ActionName).ToList();
                }
                return result;
            }
        }

        public static bool IsKeyDown(int key)
        {
            if (!_initialized || _safeMode || _vmmInput == null)
                return false;

            return _pressedKeys.Contains(key);
        }

        public static bool IsKeyPressed(int key)
        {
            if (!_initialized || _safeMode || _vmmInput == null)
                return false;

            return _pressedKeys.Contains(key) &&
                   (_previousStateBitmap[(key * 2 / 8)] & (1 << (key % 4 * 2))) == 0;
        }

        public static bool IsKeyHeldToggle(int key)
        {
            if (!_initialized || _safeMode || _vmmInput == null)
                return false;

            if (!IsKeyPressed(key))
                return _heldStates.TryGetValue(key, out var held) && held;

            var now = DateTime.UtcNow;

            lock (_eventLock)
            {
                if (_lastKeyTapTime.TryGetValue(key, out var lastTap))
                {
                    var delta = (now - lastTap).TotalMilliseconds;
                    if (delta < DoubleTapThresholdMs)
                    {
                        _heldStates[key] = !_heldStates.GetValueOrDefault(key, false);
                        _lastKeyTapTime.Remove(key);
                    }
                    else
                    {
                        _lastKeyTapTime[key] = now;
                    }
                }
                else
                {
                    _lastKeyTapTime[key] = now;
                }
            }

            return _heldStates.TryGetValue(key, out var isHeld) && isHeld;
        }

        /// <summary>
        /// InputManager Managed thread.
        /// </summary>
        private static void Worker()
        {
            Log.WriteLine("InputManager thread starting...");
            while (true)
            {
                try
                {
                    if (Memory.IsDisposed)
                        break;

                    if (!_safeMode && Memory.WaitForProcess())
                        UpdateKeys();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[InputManager] Worker thread error: {ex.Message}");
                }
                finally
                {
                    Thread.Sleep(KEY_CHECK_DELAY);
                }
            }

            Log.WriteLine("[InputManager] Worker thread exiting.");
        }

        private class KeyActionHandler
        {
            public int ActionId { get; set; }
            public string ActionName { get; set; }
            public KeyStateChangedHandler Handler { get; set; }
        }

        public class KeyEventArgs : EventArgs
        {
            public int KeyCode { get; }
            public bool IsPressed { get; }

            public KeyEventArgs(int keyCode, bool isPressed)
            {
                KeyCode = keyCode;
                IsPressed = isPressed;
            }
        }

        public delegate void KeyStateChangedHandler(object sender, KeyEventArgs e);

        #region DmaInput (private nested class)

        /// <summary>
        /// DMA-based keyboard input that works on both Windows 10 and Windows 11.
        /// Win11 (build >= 22000): resolves gafAsyncKeyStateExport via win32k session pointer chain.
        /// Win10 (build &lt; 22000):  resolves gafAsyncKeyState via win32kbase.sys EAT export.
        /// </summary>
        private sealed class DmaInput
        {
            private readonly Vmm _vmm;
            private readonly uint _winLogonPid;
            private readonly ulong _gafAsyncKeyState;
            private readonly byte[] _stateBitmap = new byte[64];
            private readonly byte[] _previousStateBitmap = new byte[64];

            public DmaInput(Vmm vmm)
            {
                _vmm = vmm;

                if (!_vmm.PidGetFromName("winlogon.exe", out _winLogonPid))
                    throw new Exception("DmaInput: failed to get winlogon.exe PID");

                int buildNumber = GetWindowsBuildNumber();
                _gafAsyncKeyState = buildNumber >= 22000
                    ? ResolveWin11KeyState()
                    : ResolveWin10KeyState();

                if (_gafAsyncKeyState == 0)
                    throw new Exception("DmaInput: failed to resolve gafAsyncKeyState");
            }

            public void UpdateKeys()
            {
                Array.Copy(_stateBitmap, _previousStateBitmap, 64);
                _vmm.MemReadSpan(_winLogonPid | Vmm.PID_PROCESS_WITH_KERNELMEMORY, _gafAsyncKeyState, _stateBitmap.AsSpan(), VmmFlags.NOCACHE);
            }

            public bool IsKeyDown(uint vkeyCode)
            {
                int idx = (int)(vkeyCode * 2 / 8);
                int bit = 1 << ((int)(vkeyCode % 4) * 2);
                return (_stateBitmap[idx] & bit) != 0;
            }

            private static int GetWindowsBuildNumber()
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false);
                    var val = key?.GetValue("CurrentBuild") as string;
                    if (int.TryParse(val, out int build))
                        return build;
                }
                catch { }
                return 22000;
            }

            private ulong ResolveWin11KeyState()
            {
                var csrssPids = _vmm.PidGetAllFromName("csrss.exe")
                    ?? throw new Exception("DmaInput: failed to enumerate csrss.exe");

                var exceptions = new List<Exception>();
                foreach (uint pid in csrssPids)
                {
                    try
                    {
                        if (!_vmm.Map_GetModuleFromName(pid, "win32ksgd.sys", out var win32kMod) &&
                            !_vmm.Map_GetModuleFromName(pid, "win32k.sys", out win32kMod))
                            throw new Exception("DmaInput: failed to get win32k module");

                        ulong win32kBase = win32kMod.vaBase;
                        ulong win32kEnd = win32kBase + win32kMod.cbImageSize;

                        ulong gSessionPtr = _vmm.FindSignature(pid, "48 8B 05 ?? ?? ?? ?? 48 8B 04 C8", win32kBase, win32kEnd);
                        if (gSessionPtr == 0)
                            gSessionPtr = _vmm.FindSignature(pid, "48 8B 05 ?? ?? ?? ?? FF C9", win32kBase, win32kEnd);
                        if (gSessionPtr == 0)
                            throw new Exception("DmaInput: gSessionPtr sig not found");

                        int relative = _vmm.MemReadValue<int>(pid, gSessionPtr + 3);
                        ulong gSessionGlobalSlots = gSessionPtr + 7 + (ulong)relative;

                        ulong userSessionState = 0;
                        for (int i = 0; i < 4; i++)
                        {
                            ulong slot0 = _vmm.MemReadValue<ulong>(pid, gSessionGlobalSlots);
                            ulong entry = _vmm.MemReadValue<ulong>(pid, slot0 + (ulong)(8 * i));
                            userSessionState = _vmm.MemReadValue<ulong>(pid, entry);
                            if (IsValidKernelVA(userSessionState))
                                break;
                        }

                        if (!_vmm.Map_GetModuleFromName(pid, "win32kbase.sys", out var baseMod))
                            throw new Exception("DmaInput: failed to get win32kbase.sys");

                        ulong baseStart = baseMod.vaBase;
                        ulong baseEnd = baseStart + baseMod.cbImageSize;
                        ulong ptr = _vmm.FindSignature(pid, "48 8D 90 ?? ?? ?? ?? E8 ?? ?? ?? ?? 0F 57 C0", baseStart, baseEnd);
                        if (ptr == 0)
                            throw new Exception("DmaInput: gafAsyncKeyStateExport sig not found");

                        uint sessionOffset = _vmm.MemReadValue<uint>(pid, ptr + 3);
                        ulong result = userSessionState + sessionOffset;
                        if (IsValidKernelVA(result))
                            return result;
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }

                throw new AggregateException("DmaInput: Win11 key state resolution failed across all csrss PIDs", exceptions);
            }

            private ulong ResolveWin10KeyState()
            {
                uint kernelPid = _winLogonPid | Vmm.PID_PROCESS_WITH_KERNELMEMORY;

                var eatEntries = _vmm.Map_GetEAT(kernelPid, "win32kbase.sys", out _);
                if (eatEntries != null)
                {
                    foreach (var entry in eatEntries)
                    {
                        if (entry.sFunction == "gafAsyncKeyState")
                            return entry.vaFunction;
                    }
                }

                return ResolveWin10KeyStateScan(kernelPid);
            }

            private ulong ResolveWin10KeyStateScan(uint kernelPid)
            {
                ulong baseStart = 0, baseEnd = 0;
                uint scanPid = kernelPid;

                if (_vmm.Map_GetModuleFromName(kernelPid, "win32kbase.sys", out var kMod))
                {
                    baseStart = kMod.vaBase;
                    baseEnd   = baseStart + kMod.cbImageSize;
                }
                else
                {
                    var csrssPids = _vmm.PidGetAllFromName("csrss.exe");
                    if (csrssPids != null)
                    {
                        foreach (uint pid in csrssPids)
                        {
                            if (_vmm.Map_GetModuleFromName(pid, "win32kbase.sys", out var mod))
                            {
                                scanPid   = pid;
                                baseStart = mod.vaBase;
                                baseEnd   = baseStart + mod.cbImageSize;
                                break;
                            }
                        }
                    }
                }

                if (baseStart == 0)
                    throw new Exception("DmaInput: failed to locate win32kbase.sys for Win10 signature scan");

                ulong ptr = _vmm.FindSignature(scanPid, "48 8B 05 ?? ?? ?? ?? 48 63 D1 0F B6 44 10 08", baseStart, baseEnd);
                if (ptr == 0)
                    ptr = _vmm.FindSignature(scanPid, "48 8B 05 ?? ?? ?? ?? 48 63 D1 0F B6 44 10 00", baseStart, baseEnd);
                if (ptr == 0)
                    ptr = _vmm.FindSignature(scanPid, "48 8B 05 ?? ?? ?? ?? 48 8B 04 C8 0F B6 04 10 83 E0 01", baseStart, baseEnd);
                if (ptr == 0)
                    throw new Exception("DmaInput: gafAsyncKeyState not found via EAT or signature scan (Win10)");

                int relative = _vmm.MemReadValue<int>(kernelPid, ptr + 3);
                ulong result = ptr + 7 + (ulong)relative;
                if (IsValidKernelVA(result))
                    return result;

                throw new Exception($"DmaInput: Win10 signature scan returned invalid kernel VA 0x{result:X}");
            }

            private static bool IsValidKernelVA(ulong va) => va > 0x7FFFFFFFFFFFUL;
        }

        #endregion
    }
}
