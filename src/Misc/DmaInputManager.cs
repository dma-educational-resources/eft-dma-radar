using VmmSharpEx;
using VmmSharpEx.Extensions;
using VmmSharpEx.Options;

namespace eft_dma_radar.Misc
{
    /// <summary>
    /// DMA-based keyboard input manager that works on both Windows 10 and Windows 11.
    /// Win11 (build >= 22000): resolves gafAsyncKeyStateExport via win32k session pointer chain.
    /// Win10 (build < 22000):  resolves gafAsyncKeyState via win32kbase.sys EAT export.
    /// </summary>
    internal sealed class DmaInputManager
    {
        private readonly Vmm _vmm;
        private readonly uint _winLogonPid;
        private readonly ulong _gafAsyncKeyState;
        private readonly byte[] _stateBitmap = new byte[64];
        private readonly byte[] _previousStateBitmap = new byte[64];

        /// <summary>
        /// Initializes the DMA input manager. Throws <see cref="Exception"/> on failure.
        /// </summary>
        public DmaInputManager(Vmm vmm)
        {
            _vmm = vmm;

            if (!_vmm.PidGetFromName("winlogon.exe", out _winLogonPid))
                throw new Exception("DmaInputManager: failed to get winlogon.exe PID");

            int buildNumber = GetWindowsBuildNumber();
            _gafAsyncKeyState = buildNumber >= 22000
                ? ResolveWin11KeyState()
                : ResolveWin10KeyState();

            if (_gafAsyncKeyState == 0)
                throw new Exception("DmaInputManager: failed to resolve gafAsyncKeyState");
        }

        /// <summary>
        /// Reads the current keyboard state from the target machine. Call periodically before <see cref="IsKeyDown"/>.
        /// </summary>
        public void UpdateKeys()
        {
            Array.Copy(_stateBitmap, _previousStateBitmap, 64);
            _vmm.MemReadSpan(_winLogonPid | Vmm.PID_PROCESS_WITH_KERNELMEMORY, _gafAsyncKeyState, _stateBitmap.AsSpan(), VmmFlags.NOCACHE);
        }

        /// <summary>Returns true if the given virtual key is currently held down.</summary>
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
            // Fallback: assume Win11 so the same code path as before is used
            return 22000;
        }

        // Win11: gafAsyncKeyStateExport lives inside a session structure.
        // Chain: win32ksgd.sys/win32k.sys -> gSessionGlobalSlots -> userSessionState -> win32kbase.sys offset
        private ulong ResolveWin11KeyState()
        {
            var csrssPids = _vmm.PidGetAllFromName("csrss.exe")
                ?? throw new Exception("DmaInputManager: failed to enumerate csrss.exe");

            var exceptions = new List<Exception>();
            foreach (uint pid in csrssPids)
            {
                try
                {
                    if (!_vmm.Map_GetModuleFromName(pid, "win32ksgd.sys", out var win32kMod) &&
                        !_vmm.Map_GetModuleFromName(pid, "win32k.sys", out win32kMod))
                        throw new Exception("DmaInputManager: failed to get win32k module");

                    ulong win32kBase = win32kMod.vaBase;
                    ulong win32kEnd  = win32kBase + win32kMod.cbImageSize;

                    // Try win32ksgd signature first, then win32k fallback
                    ulong gSessionPtr = _vmm.FindSignature(pid, "48 8B 05 ?? ?? ?? ?? 48 8B 04 C8", win32kBase, win32kEnd);
                    if (gSessionPtr == 0)
                        gSessionPtr = _vmm.FindSignature(pid, "48 8B 05 ?? ?? ?? ?? FF C9", win32kBase, win32kEnd);
                    if (gSessionPtr == 0)
                        throw new Exception("DmaInputManager: gSessionPtr sig not found");

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
                        throw new Exception("DmaInputManager: failed to get win32kbase.sys");

                    ulong baseStart = baseMod.vaBase;
                    ulong baseEnd   = baseStart + baseMod.cbImageSize;
                    ulong ptr = _vmm.FindSignature(pid, "48 8D 90 ?? ?? ?? ?? E8 ?? ?? ?? ?? 0F 57 C0", baseStart, baseEnd);
                    if (ptr == 0)
                        throw new Exception("DmaInputManager: gafAsyncKeyStateExport sig not found");

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

            throw new AggregateException("DmaInputManager: Win11 key state resolution failed across all csrss PIDs", exceptions);
        }

        // Win10: gafAsyncKeyState is a direct export of win32kbase.sys in winlogon's kernel memory.
        private ulong ResolveWin10KeyState()
        {
            uint kernelPid = _winLogonPid | Vmm.PID_PROCESS_WITH_KERNELMEMORY;
            var eatEntries = _vmm.Map_GetEAT(kernelPid, "win32kbase.sys", out _);
            if (eatEntries == null)
                throw new Exception("DmaInputManager: Map_GetEAT failed for win32kbase.sys");

            foreach (var entry in eatEntries)
            {
                if (entry.sFunction == "gafAsyncKeyState")
                    return entry.vaFunction;
            }

            throw new Exception("DmaInputManager: gafAsyncKeyState not found in win32kbase.sys EAT");
        }

        private static bool IsValidKernelVA(ulong va) => va > 0x7FFFFFFFFFFFUL;
    }
}
