using System.Collections.Concurrent;
using System.Text;
using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Misc;
using eft_dma_radar.Tarkov;
using Vmmsharp;

namespace eft_dma_radar.Common.Misc
{
    public static class InputManager
    {
        private static bool _initialized = false;
        private static bool _safeMode = false;

        private static ulong _gafAsyncKeyStateExport;

        private static byte[] _currentStateBitmap = new byte[64];
        private static byte[] _previousStateBitmap = new byte[64];
        private static readonly ConcurrentDictionary<int, byte> _pressedKeys = new ConcurrentDictionary<int, byte>();

        private static Vmm _hVMM;
        private static VmmProcess _winLogon;

        private static int _initAttempts = 0;
        private const int MAX_ATTEMPTS = 3;
        private const int DELAY = 500;
        private const int KEY_CHECK_DELAY = 100; // in milliseconds

        private static int _currentBuild;
        private static int _updateBuildRevision;
        private static readonly Dictionary<int, DateTime> _lastKeyTapTime = new();
        private static readonly Dictionary<int, bool> _heldStates = new();
        private const int DoubleTapThresholdMs = 300;

        public static bool IsReady => _initialized;

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
                if (MemoryInterface.Memory?.VmmHandle == null)
                {
                    _safeMode = true;
                    XMLogging.WriteLine("[InputManager] Starting in Safe Mode - Input functionality disabled");
                    NotificationsShared.Warning("[InputManager] Safe Mode - Input functionality disabled");
                    return;
                }

                _hVMM = MemoryInterface.Memory.VmmHandle;

                if (_hVMM != null)
                {
                    new Thread(Worker)
                    {
                        IsBackground = true
                    }.Start();
                }

                if (InputManager.InitKeyboard())
                {
                    XMLogging.WriteLine("[InputManager] Initialized");
                    NotificationsShared.Success("[InputManager] Initialized successfully!");
                }
                else
                {
                    XMLogging.WriteLine("ERROR Initializing Input Manager");
                    NotificationsShared.Error("[InputManager] Failed to initialize, you may need to restart your gaming pc for hotkeys to work.");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[InputManager] Error during initialization: {ex.Message}");
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
                XMLogging.WriteLine("[InputManager] Skipping keyboard initialization - Safe Mode");
                return false;
            }

            try
            {
                var currentBuild = _hVMM.RegValueRead("HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\CurrentBuild", out _);
                _currentBuild = int.Parse(Encoding.Unicode.GetString(currentBuild));

                var UBR = _hVMM.RegValueRead("HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\UBR", out _);
                _updateBuildRevision = BitConverter.ToInt32(UBR);

                var tmpProcess = _hVMM.Process("winlogon.exe");
                _winLogon = _hVMM.Process(tmpProcess.PID | Vmm.PID_PROCESS_WITH_KERNELMEMORY);

                if (_winLogon == null)
                {
                    XMLogging.WriteLine("Winlogon process not found");
                    _initAttempts++;
                    return false;
                }

                return _currentBuild > 22000 ? InputManager.InitKeyboardForNewWindows() : InputManager.InitKeyboardForOldWindows();
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Error initializing keyboard: {ex.Message}\n{ex.StackTrace}");
                _initAttempts++;
                return false;
            }
        }

        private static VmmProcess.ModuleEntry GetModuleInfo(VmmProcess process, string moduleToFind)
        {
            var modules = process.MapModule();
            var moduleLower = moduleToFind.ToLower();

            foreach (var module in modules)
            {
                if (module.sFullName.ToLower().Contains(moduleLower))
                {
                    XMLogging.WriteLine($"Found module: {module.sFullName}");
                    return module;
                }
            }

            return new VmmProcess.ModuleEntry();
        }

        private static bool InitKeyboardForNewWindows()
        {
            if (_safeMode || _hVMM == null)
                return false;

            XMLogging.WriteLine("Windows version > 22000, attempting signature-based approach");

            var csrssProcesses = _hVMM.Processes.Where(p => p.Name.Equals("csrss.exe", StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var csrss in csrssProcesses)
            {
                try
                {
                    // Get win32k module info
                    if (!TryGetWin32kInfo(csrss, out ulong win32kBase, out ulong win32kSize))
                        continue;

                    // Find session globals pointer
                    if (!TryFindSessionPointer(csrss, win32kBase, win32kSize, out ulong gSessionGlobalSlots))
                        continue;

                    // Resolve user session state
                    if (!TryResolveUserSessionState(csrss, gSessionGlobalSlots, out ulong userSessionState))
                        continue;

                    // Get async key state offset
                    if (!TryGetAsyncKeyStateOffset(csrss, userSessionState, out ulong keyStateAddress))
                        continue;

                    _gafAsyncKeyStateExport = keyStateAddress;
                    _initialized = true;
                    return true;
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"KEYBOARD ERR: {ex.Message}\n{ex.StackTrace}");
                }
            }

            _initAttempts++;
            XMLogging.WriteLine("Failed to initialize keyboard handler for new Windows version");
            return false;
        }

        private static bool TryGetWin32kInfo(VmmProcess process, out ulong baseAddress, out ulong moduleSize)
        {
            baseAddress = 0;
            moduleSize = 0;

            baseAddress = process.GetModuleBase("win32ksgd.sys");

            if (baseAddress != 0)
            {
                var moduleInfo = GetModuleInfo(process, "win32ksgd.sys");
                moduleSize = moduleInfo.cbImageSize;
                return true;
            }

            baseAddress = process.GetModuleBase("win32k.sys");

            if (baseAddress != 0)
            {
                var moduleInfo = GetModuleInfo(process, "win32k.sys");
                moduleSize = moduleInfo.cbImageSize;
                return true;
            }

            XMLogging.WriteLine("Failed to get module win32k info");
            return false;
        }

        private static bool TryFindSessionPointer(VmmProcess process, ulong baseAddr, ulong size, out ulong sessionPtr)
        {
            sessionPtr = 0;

            if (_safeMode || MemoryInterface.Memory == null)
            {
                XMLogging.WriteLine("[InputManager] Skipping signature search - Safe Mode");
                return false;
            }

            var gSessionPtr = MemoryInterface.Memory.FindSignature("48 8B 05 ? ? ? ? 48 8B 04 C8", baseAddr, baseAddr + size, process);

            if (gSessionPtr == 0)
            {
                gSessionPtr = MemoryInterface.Memory.FindSignature("48 8B 05 ? ? ? ? FF C9", baseAddr, baseAddr + size, process);
                if (gSessionPtr == 0)
                {
                    XMLogging.WriteLine("Failed to find g_session_global_slots");
                    return false;
                }
            }

            var relativeOffsetResult = process.MemReadAs<int>(gSessionPtr + 3);

            if (relativeOffsetResult.Value == 0)
            {
                XMLogging.WriteLine("Failed to read relative offset");
                return false;
            }

            sessionPtr = gSessionPtr + 7 + (ulong)relativeOffsetResult.Value;

            return true;
        }

        private static bool TryResolveUserSessionState(VmmProcess process, ulong sessionPtr, out ulong sessionState)
        {
            sessionState = 0;

            for (int i = 0; i < 8; i++)
            {
                var t1 = process.MemReadAs<ulong>(sessionPtr);
                if (t1.Value == 0)
                    continue;

                var t2 = process.MemReadAs<ulong>(t1.Value + (ulong)(8 * i));
                if (t2.Value == 0)
                    continue;

                var t3 = process.MemReadAs<ulong>(t2.Value);
                if (t3.Value == 0)
                    continue;

                sessionState = t3.Value;

                if (sessionState > 0x7FFFFFFFFFFF)
                    return true;
            }

            return sessionState != 0;
        }

        private static bool TryGetAsyncKeyStateOffset(VmmProcess process, ulong sessionState, out ulong keyStateAddr)
        {
            keyStateAddr = 0;

            var win32kbaseBase = process.GetModuleBase("win32kbase.sys");

            if (win32kbaseBase == 0)
            {
                XMLogging.WriteLine("Failed to get module win32kbase info");
                return false;
            }

            var win32kbaseInfo = GetModuleInfo(process, "win32kbase.sys");
            var win32kbaseSize = win32kbaseInfo.cbImageSize;

            if (_safeMode || MemoryInterface.Memory == null)
            {
                XMLogging.WriteLine("[InputManager] Skipping signature search - Safe Mode");
                return false;
            }

            var ptr = MemoryInterface.Memory.FindSignature(
                "48 8D 90 ? ? ? ? E8 ? ? ? ? 0F 57 C0",
                win32kbaseBase,
                win32kbaseBase + win32kbaseSize,
                process);

            if (ptr == 0)
            {
                XMLogging.WriteLine("Failed to find offset for gafAsyncKeyStateExport");
                return false;
            }

            var offsetResult = process.MemReadAs<uint>(ptr + 3);

            if (offsetResult.Value == 0)
            {
                XMLogging.WriteLine("Failed to read session offset");
                return false;
            }

            keyStateAddr = sessionState + offsetResult.Value;

            return keyStateAddr > 0x7FFFFFFFFFFF;
        }

        private static bool InitKeyboardForOldWindows()
        {
            if (_safeMode || _winLogon == null)
                return false;

            XMLogging.WriteLine("Older Windows version detected, attempting to resolve via EAT");

            var exports = _winLogon.MapModuleEAT("win32kbase.sys");
            var gafAsyncKeyStateExport = exports.FirstOrDefault(e => e.sFunction == "gafAsyncKeyState");

            if (!string.IsNullOrEmpty(gafAsyncKeyStateExport.sFunction) && gafAsyncKeyStateExport.vaFunction >= 0x7FFFFFFFFFFF)
            {
                _gafAsyncKeyStateExport = gafAsyncKeyStateExport.vaFunction;
                _initialized = true;
                XMLogging.WriteLine("Resolved export via EAT");
                return true;
            }

            XMLogging.WriteLine("Failed to resolve via EAT, attempting to resolve with PDB");

            var pdb = _winLogon.Pdb("win32kbase.sys");

            if (pdb != null && pdb.SymbolAddress("gafAsyncKeyState", out ulong gafAsyncKeyState))
            {
                if (gafAsyncKeyState >= 0x7FFFFFFFFFFF)
                {
                    _gafAsyncKeyStateExport = gafAsyncKeyState;
                    _initialized = true;
                    XMLogging.WriteLine("Resolved export via PDB");
                    return true;
                }
            }

            XMLogging.WriteLine("Failed to find export");
            return false;
        }

        public static unsafe void UpdateKeys()
        {
            if (!_initialized || _safeMode || _winLogon == null)
                return;

            Array.Copy(_currentStateBitmap, _previousStateBitmap, 64);

            fixed (byte* pb = _currentStateBitmap)
            {
                var success = _winLogon.MemRead(
                    _gafAsyncKeyStateExport,
                    pb,
                    64,
                    out _,
                    Vmm.FLAG_NOCACHE
                );

                if (!success)
                    return;

                _pressedKeys.Clear();

                for (int vk = 0; vk < 256; ++vk)
                {
                    if ((_currentStateBitmap[(vk * 2 / 8)] & 1 << vk % 4 * 2) != 0)
                        _pressedKeys.AddOrUpdate(vk, 1, (oldkey, oldvalue) => 1);
                }

                for (int vk = 0; vk < 256; ++vk)
                {
                    var wasDown = (_previousStateBitmap[(vk * 2 / 8)] & (1 << (vk % 4 * 2))) != 0;
                    var isDown = (_currentStateBitmap[(vk * 2 / 8)] & (1 << (vk % 4 * 2))) != 0;

                    if (wasDown != isDown)
                    {
                        lock (_eventLock)
                        {
                            if (_keyActionHandlers.TryGetValue(vk, out var handlers))
                            {
                                foreach (var handler in handlers.ToList())
                                {
                                    try
                                    {
                                        handler.Handler?.Invoke(null, new KeyEventArgs(vk, isDown));
                                    }
                                    catch (Exception ex)
                                    {
                                        XMLogging.WriteLine($"Error executing key handler for action '{handler.ActionName}': {ex.Message}");
                                    }
                                }
                            }
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
                XMLogging.WriteLine($"[InputManager] RegisterKeyAction skipped - Safe Mode or not ready");
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
            if (!_initialized || _safeMode || _gafAsyncKeyStateExport < 0x7FFFFFFFFFFF)
                return false;

            var virtualKeyCode = (int)key;
            return _pressedKeys.ContainsKey(virtualKeyCode);
        }

        public static bool IsKeyPressed(int key)
        {
            if (!_initialized || _safeMode || _gafAsyncKeyStateExport < 0x7FFFFFFFFFFF)
                return false;

            var virtualKeyCode = (int)key;
            return _pressedKeys.ContainsKey(virtualKeyCode) &&
                   (_previousStateBitmap[(virtualKeyCode * 2 / 8)] & (1 << (virtualKeyCode % 4 * 2))) == 0;
        }

        public static bool IsKeyHeldToggle(int key)
        {
            if (!_initialized || _safeMode || _gafAsyncKeyStateExport < 0x7FFFFFFFFFFF)
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
            XMLogging.WriteLine("InputManager thread starting...");
            while (true)
            {
                try
                {
                    if (!_safeMode && MemDMABase.WaitForProcess())
                        UpdateKeys();
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[InputManager] Worker thread error: {ex.Message}");
                }
                finally
                {
                    Thread.Sleep(KEY_CHECK_DELAY);
                }
            }
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
    }
}