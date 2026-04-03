using VmmSharpEx.Options;

namespace VmmSharpEx.Extensions.Input
{
    /// <summary>
    /// DMA-based keyboard input that works on both Windows 10 and Windows 11.
    /// <para>
    /// Win11 (build ≥ 22000): resolves gafAsyncKeyStateExport via win32k session pointer chain.
    /// </para>
    /// <para>
    /// Win10 (build &lt; 22000): resolves gafAsyncKeyState via win32kbase.sys EAT export or signature scan.
    /// </para>
    /// </summary>
    public sealed class VmmInputManagerEx
    {
        private readonly Vmm _vmm;
        private readonly uint _winLogonPid;
        private readonly ulong _gafAsyncKeyState;
        private readonly byte[] _stateBitmap = new byte[64];
        private readonly byte[] _previousStateBitmap = new byte[64];

        public VmmInputManagerEx(Vmm vmm)
        {
            _vmm = vmm ?? throw new ArgumentNullException(nameof(vmm));

            if (!_vmm.PidGetFromName("winlogon.exe", out _winLogonPid))
                throw new Exception("VmmInputManagerEx: failed to get winlogon.exe PID");

            int buildNumber = GetTargetBuildNumber();

            // Try the expected path first based on the target OS build number,
            // then fall back to the other path. This handles misdetection and
            // edge-case builds (e.g. Win10 with win32ksgd.sys backport).
            if (buildNumber >= 22000)
            {
                if (!TryResolveWin11KeyState(out _gafAsyncKeyState))
                    TryResolveWin10KeyState(out _gafAsyncKeyState);
            }
            else
            {
                if (!TryResolveWin10KeyState(out _gafAsyncKeyState))
                    TryResolveWin11KeyState(out _gafAsyncKeyState);
            }

            if (_gafAsyncKeyState == 0)
                throw new Exception("VmmInputManagerEx: failed to resolve gafAsyncKeyState via Win11 or Win10 methods");
        }

        /// <summary>
        /// Reads the current key state bitmap from the target machine's kernel memory.
        /// Call once per polling cycle before checking <see cref="IsKeyDown"/>.
        /// </summary>
        public void UpdateKeys()
        {
            Array.Copy(_stateBitmap, _previousStateBitmap, 64);
            _vmm.MemReadSpan(
                _winLogonPid | Vmm.PID_PROCESS_WITH_KERNELMEMORY,
                _gafAsyncKeyState,
                _stateBitmap.AsSpan(),
                VmmFlags.NOCACHE);
        }

        /// <summary>
        /// Returns <see langword="true"/> if the specified virtual-key code is currently held down.
        /// </summary>
        public bool IsKeyDown(uint vkeyCode)
        {
            int idx = (int)(vkeyCode * 2 / 8);
            int bit = 1 << ((int)(vkeyCode % 4) * 2);
            return (_stateBitmap[idx] & bit) != 0;
        }

        #region Win11 Resolution

        private bool TryResolveWin11KeyState(out ulong result)
        {
            result = 0;

            uint[] csrssPids;
            try { csrssPids = _vmm.PidGetAllFromName("csrss.exe"); }
            catch { return false; }
            if (csrssPids is null || csrssPids.Length == 0)
                return false;

            foreach (uint pid in csrssPids)
            {
                try
                {
                    if (!_vmm.Map_GetModuleFromName(pid, "win32ksgd.sys", out var win32kMod) &&
                        !_vmm.Map_GetModuleFromName(pid, "win32k.sys", out win32kMod))
                        continue;

                    ulong win32kBase = win32kMod.vaBase;
                    ulong win32kEnd = win32kBase + win32kMod.cbImageSize;

                    ulong gSessionPtr = _vmm.FindSignature(pid, "48 8B 05 ?? ?? ?? ?? 48 8B 04 C8", win32kBase, win32kEnd);
                    if (gSessionPtr == 0)
                        gSessionPtr = _vmm.FindSignature(pid, "48 8B 05 ?? ?? ?? ?? FF C9", win32kBase, win32kEnd);
                    if (gSessionPtr == 0)
                        continue;

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
                        continue;

                    ulong baseStart = baseMod.vaBase;
                    ulong baseEnd = baseStart + baseMod.cbImageSize;
                    ulong ptr = _vmm.FindSignature(pid, "48 8D 90 ?? ?? ?? ?? E8 ?? ?? ?? ?? 0F 57 C0", baseStart, baseEnd);
                    if (ptr == 0)
                        continue;

                    uint sessionOffset = _vmm.MemReadValue<uint>(pid, ptr + 3);
                    ulong candidate = userSessionState + sessionOffset;
                    if (IsValidKernelVA(candidate))
                    {
                        result = candidate;
                        return true;
                    }
                }
                catch
                {
                    // Try next csrss PID
                }
            }

            return false;
        }

        #endregion

        #region Win10 Resolution

        private bool TryResolveWin10KeyState(out ulong result)
        {
            result = 0;
            try
            {
                uint kernelPid = _winLogonPid | Vmm.PID_PROCESS_WITH_KERNELMEMORY;

                var eatEntries = _vmm.Map_GetEAT(kernelPid, "win32kbase.sys", out _);
                if (eatEntries != null)
                {
                    foreach (var entry in eatEntries)
                    {
                        if (entry.sFunction == "gafAsyncKeyState")
                        {
                            result = entry.vaFunction;
                            return true;
                        }
                    }
                }

                return TryResolveWin10KeyStateScan(kernelPid, out result);
            }
            catch
            {
                return false;
            }
        }

        private bool TryResolveWin10KeyStateScan(uint kernelPid, out ulong result)
        {
            result = 0;
            ulong baseStart = 0, baseEnd = 0;
            uint scanPid = kernelPid;

            if (_vmm.Map_GetModuleFromName(kernelPid, "win32kbase.sys", out var kMod))
            {
                baseStart = kMod.vaBase;
                baseEnd = baseStart + kMod.cbImageSize;
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
                            scanPid = pid;
                            baseStart = mod.vaBase;
                            baseEnd = baseStart + mod.cbImageSize;
                            break;
                        }
                    }
                }
            }

            if (baseStart == 0)
                return false;

            ulong ptr = _vmm.FindSignature(scanPid, "48 8B 05 ?? ?? ?? ?? 48 63 D1 0F B6 44 10 08", baseStart, baseEnd);
            if (ptr == 0)
                ptr = _vmm.FindSignature(scanPid, "48 8B 05 ?? ?? ?? ?? 48 63 D1 0F B6 44 10 00", baseStart, baseEnd);
            if (ptr == 0)
                ptr = _vmm.FindSignature(scanPid, "48 8B 05 ?? ?? ?? ?? 48 8B 04 C8 0F B6 04 10 83 E0 01", baseStart, baseEnd);
            if (ptr == 0)
                return false;

            int relative = _vmm.MemReadValue<int>(kernelPid, ptr + 3);
            ulong candidate = ptr + 7 + (ulong)relative;
            if (IsValidKernelVA(candidate))
            {
                result = candidate;
                return true;
            }

            return false;
        }

        #endregion

        /// <summary>
        /// Reads the Windows build number from the <b>target</b> machine's registry
        /// via DMA (<see cref="Vmm.WinReg_QueryValue"/>).
        /// Falls back to the local host registry if the DMA read fails.
        /// </summary>
        private int GetTargetBuildNumber()
        {
            // Prefer reading the target's registry through VMM.
            try
            {
                const string regPath = "HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\CurrentBuild";
                var data = _vmm.WinReg_QueryValue(regPath, out _);
                if (data is { Length: > 0 })
                {
                    // REG_SZ comes back as a null-terminated UTF-16 byte array.
                    string val = System.Text.Encoding.Unicode.GetString(data).TrimEnd('\0');
                    if (int.TryParse(val, out int build))
                        return build;
                }
            }
            catch { }

            // Fallback: read the local (host) registry.
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false);
                var val = key?.GetValue("CurrentBuild") as string;
                if (int.TryParse(val, out int build))
                    return build;
            }
            catch { }

            // Default to Win11 so the more robust Win11 path is tried first.
            return 22000;
        }

        private static bool IsValidKernelVA(ulong va) => va > 0x7FFFFFFFFFFFUL;
    }
}
