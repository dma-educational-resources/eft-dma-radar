using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Misc;
using eft_dma_radar.Tarkov;
using VmmSharpEx;

namespace eft_dma_radar.Common.Misc
{
    public static class InputManager
    {
        private static volatile bool _initialized = false;
        private static bool _safeMode = false;

        private static byte[] _currentStateBitmap = new byte[64];
        private static byte[] _previousStateBitmap = new byte[64];
        private static readonly HashSet<int> _pressedKeys = new();

        private static Vmm _hVMM;
        private static DmaInputManager _vmmInput;

        private static int _initAttempts = 0;
        private const int MAX_ATTEMPTS = 3;
        private const int DELAY = 500;
        private const int KEY_CHECK_DELAY = 100; // in milliseconds

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
                _vmmInput = new DmaInputManager(_hVMM);
                _initialized = true;
                return true;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Error initializing keyboard: {ex.Message}\n{ex.StackTrace}");
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

                if (isDown)
                    _pressedKeys.Add(vk);

                var wasDown = (_previousStateBitmap[(vk * 2 / 8)] & (1 << (vk % 4 * 2))) != 0;

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
                            XMLogging.WriteLine($"Error executing key handler for action '{handler.ActionName}': {ex.Message}");
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
            XMLogging.WriteLine("InputManager thread starting...");
            while (true)
            {
                try
                {
                    if (MemoryInterface.Memory is { IsDisposed: true })
                        break;

                    if (!_safeMode && MemDMABase.WaitForProcess())
                        UpdateKeys();
                }
                catch (ObjectDisposedException)
                {
                    break;
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

            XMLogging.WriteLine("[InputManager] Worker thread exiting.");
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