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
                    ulong win32kEnd = win32kBase + win32kMod.cbImageSize;

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
                    ulong baseEnd = baseStart + baseMod.cbImageSize;
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

        // Win10: gafAsyncKeyState is a direct export of win32kbase.sys.
        // On some builds (e.g. 19045) it is not in the EAT — fall back to signature scan.
        private ulong ResolveWin10KeyState()
        {
            uint kernelPid = _winLogonPid | Vmm.PID_PROCESS_WITH_KERNELMEMORY;

            // Try EAT first (present on many Win10 builds)
            var eatEntries = _vmm.Map_GetEAT(kernelPid, "win32kbase.sys", out _);
            if (eatEntries != null)
            {
                foreach (var entry in eatEntries)
                {
                    if (entry.sFunction == "gafAsyncKeyState")
                        return entry.vaFunction;
                }
            }

            // EAT export absent — fall back to RIP-relative signature scan
            return ResolveWin10KeyStateScan(kernelPid);
        }

        /// <summary>
        /// Win10 fallback: scans win32kbase.sys for the RIP-relative MOV that loads gafAsyncKeyState.
        /// Handles builds (e.g. 19045) where the symbol is present but not EAT-exported.
        /// </summary>
        private ulong ResolveWin10KeyStateScan(uint kernelPid)
        {
            // Locate win32kbase.sys — try winlogon+kernel context first, then csrss.exe
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
                throw new Exception("DmaInputManager: failed to locate win32kbase.sys for Win10 signature scan");

            // Win10 19041-19045 typical pattern:
            //   48 8B 05 xx xx xx xx   mov rax, [rip+rel32]   rip+7+rel32 = &gafAsyncKeyState
            //   48 63 D1               movsxd rdx, ecx
            //   0F B6 44 10 08         movzx eax, byte [rax+rdx+8]
            ulong ptr = _vmm.FindSignature(scanPid, "48 8B 05 ?? ?? ?? ?? 48 63 D1 0F B6 44 10 08", baseStart, baseEnd);
            if (ptr == 0)
                ptr = _vmm.FindSignature(scanPid, "48 8B 05 ?? ?? ?? ?? 48 63 D1 0F B6 44 10 00", baseStart, baseEnd);
            if (ptr == 0)
                ptr = _vmm.FindSignature(scanPid, "48 8B 05 ?? ?? ?? ?? 48 8B 04 C8 0F B6 04 10 83 E0 01", baseStart, baseEnd);
            if (ptr == 0)
                throw new Exception("DmaInputManager: gafAsyncKeyState not found via EAT or signature scan (Win10)");

            int relative = _vmm.MemReadValue<int>(kernelPid, ptr + 3);
            ulong result = ptr + 7 + (ulong)relative;
            if (IsValidKernelVA(result))
                return result;

            throw new Exception($"DmaInputManager: Win10 signature scan returned invalid kernel VA 0x{result:X}");
        }

        private static bool IsValidKernelVA(ulong va) => va > 0x7FFFFFFFFFFFUL;
    }
}
