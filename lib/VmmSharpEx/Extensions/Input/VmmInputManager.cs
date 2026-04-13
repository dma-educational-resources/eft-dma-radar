/*  
 *  VmmSharpEx by Lone (Lone DMA)
 *  Copyright (C) 2025 AGPL-3.0
*/

/*
MIT License

Copyright (c) 2023 Metick

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
 */

// Thanks to Metick for the original implementation! https://github.com/Metick/DMALibrary/blob/Master/DMALibrary/Memory/InputManager.cpp

using System.Runtime.CompilerServices;

namespace VmmSharpEx.Extensions.Input
{
    /// <summary>
    /// Extension class that queries user input state via Win32 Kernel Interop (Read-Only).
    /// <para>
    /// Supports both Windows 10 and Windows 11.
    /// Win11 (build ≥ 22000): resolves gafAsyncKeyStateExport via win32k session pointer chain.
    /// Win10 (build &lt; 22000): resolves gafAsyncKeyState via win32kbase.sys EAT export or signature scan.
    /// </para>
    /// </summary>
    public sealed class VmmInputManager
    {
        private readonly Vmm _vmm;
        private readonly Action<string>? _log;

        private readonly byte[] _stateBitmap = new byte[64];
        private readonly byte[] _previousStateBitmap = new byte[256 / 8];
        private readonly ulong _gafAsyncKeyStateExport;
        private readonly uint _winLogonPid;

        /// <summary>The target machine's Windows build number detected during initialization.</summary>
        public int TargetBuildNumber { get; }

        /// <summary>The resolution method that was used to find gafAsyncKeyState.</summary>
        public string ResolutionMethod { get; private set; } = "Unknown";

        private VmmInputManager() { throw new NotImplementedException(); }

        /// <summary>
        /// Extension class that queries user input state via Win32 Kernel Interop (Read-Only).
        /// Supports both Windows 10 and Windows 11.
        /// </summary>
        /// <param name="vmm">Parent VMM Instance (must be already initialized).</param>
        /// <param name="log">
        /// Optional diagnostic logging callback. When provided, receives detailed
        /// messages about each resolution step for troubleshooting.
        /// </param>
        /// <exception cref="VmmException"></exception>
        public VmmInputManager(Vmm vmm, Action<string>? log = null)
        {
            _vmm = vmm;
            _log = log;

            if (!_vmm.PidGetFromName("winlogon.exe", out _winLogonPid))
                throw new VmmException("Failed to get winlogon.exe PID");

            TargetBuildNumber = GetTargetBuildNumber();
            _log?.Invoke($"Target build: {TargetBuildNumber}, winlogon PID: {_winLogonPid}");

            // Try the expected path first based on the target OS build number,
            // then fall back to the other path. This handles misdetection and
            // edge-case builds (e.g. Win10 with win32ksgd.sys backport).
            ulong gafAsyncKeyState = 0;
            if (TargetBuildNumber >= 22000)
            {
                _log?.Invoke("Attempting Win11 resolution (build >= 22000)...");
                if (!TryResolveWin11KeyState(out gafAsyncKeyState))
                {
                    _log?.Invoke("Win11 failed, falling back to Win10...");
                    TryResolveWin10KeyState(out gafAsyncKeyState);
                }
            }
            else
            {
                _log?.Invoke($"Attempting Win10 resolution (build {TargetBuildNumber})...");
                if (!TryResolveWin10KeyState(out gafAsyncKeyState))
                {
                    _log?.Invoke("Win10 failed, falling back to Win11...");
                    TryResolveWin11KeyState(out gafAsyncKeyState);
                }
            }

            if (gafAsyncKeyState == 0)
                throw new VmmException($"Failed to resolve gafAsyncKeyState via Win11 or Win10 methods (build {TargetBuildNumber})");

            _gafAsyncKeyStateExport = gafAsyncKeyState;
            _log?.Invoke($"Resolved gafAsyncKeyState @ 0x{gafAsyncKeyState:X} via {ResolutionMethod}");
        }

        /// <summary>
        /// Updates the internal key state bitmap. Call this method periodically to refresh key states.
        /// </summary>
        /// <remarks>
        /// Typically you would call this before you make <see cref="IsKeyDown(uint)"/> calls.
        /// </remarks>
        /// <exception cref="VmmException"></exception>
        public void UpdateKeys()
        {
            Span<byte> previousKeyStateBitmap = stackalloc byte[64];
            _stateBitmap.CopyTo(previousKeyStateBitmap);

            // Read 64 bytes from gafAsyncKeyStateExport
            if (!_vmm.MemReadSpan(_winLogonPid | Vmm.PID_PROCESS_WITH_KERNELMEMORY, _gafAsyncKeyStateExport, _stateBitmap, VmmSharpEx.Options.VmmFlags.NOCACHE))
                throw new VmmException("Failed to read key state bitmap.");

            for (int vk = 0; vk < 256; ++vk)
                if ((_stateBitmap[(vk * 2 / 8)] & (1 << ((vk % 4) * 2))) != 0 && (previousKeyStateBitmap[(vk * 2 / 8)] & (1 << ((vk % 4) * 2))) == 0)
                    _previousStateBitmap[vk / 8] |= (byte)(1 << (vk % 8));
        }

        /// <summary>
        /// Checks if a given Virtual Key is currently down.
        /// See: <see href="https://learn.microsoft.com/windows/win32/inputdev/virtual-key-codes"/>
        /// </summary>
        /// <remarks>
        /// Recommend calling <see cref="UpdateKeys"/> before calling this method to ensure the key states are up-to-date.
        /// </remarks>
        /// <param name="vkey">Windows virtual key.</param>
        /// <returns><see langword="true"/> if key is down, otherwise <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsKeyDown(Win32VirtualKey vkey) => IsKeyDown((uint)vkey);

        /// <summary>
        /// Checks if a given Virtual Key is currently down.
        /// See: <see href="https://learn.microsoft.com/windows/win32/inputdev/virtual-key-codes"/>
        /// </summary>
        /// <remarks>
        /// Recommend calling <see cref="UpdateKeys"/> before calling this method to ensure the key states are up-to-date.
        /// </remarks>
        /// <param name="vkeyCode">Windows virtual key code.</param>
        /// <returns><see langword="true"/> if key is down, otherwise <see langword="false"/>.</returns>
        public bool IsKeyDown(uint vkeyCode)
        {
            if (!_gafAsyncKeyStateExport.IsValidKernelVA())
                return false;
            int idx = (int)(vkeyCode * 2 / 8);
            int bit = 1 << ((int)vkeyCode % 4 * 2);
            return (_stateBitmap[idx] & bit) != 0;
        }

        #region Win11 Resolution

        private bool TryResolveWin11KeyState(out ulong result)
        {
            result = 0;

            uint[]? csrssPids;
            try { csrssPids = _vmm.PidGetAllFromName("csrss.exe"); }
            catch { return false; }
            if (csrssPids is null || csrssPids.Length == 0)
                return false;

            foreach (uint pid in csrssPids)
            {
                try
                {
                    if (!_vmm.Map_GetModuleFromName(pid, "win32ksgd.sys", out var win32kModuleInfo))
                    {
                        if (!_vmm.Map_GetModuleFromName(pid, "win32k.sys", out win32kModuleInfo))
                            continue;
                    }
                    ulong win32kBase = win32kModuleInfo.vaBase;
                    ulong win32kSize = win32kModuleInfo.cbImageSize;

                    ulong gSessionPtr = _vmm.FindSignature(pid, "48 8B 05 ?? ?? ?? ?? 48 8B 04 C8", win32kBase, win32kBase + win32kSize);
                    if (gSessionPtr == 0)
                    {
                        gSessionPtr = _vmm.FindSignature(pid, "48 8B 05 ?? ?? ?? ?? FF C9", win32kBase, win32kBase + win32kSize);
                        if (gSessionPtr == 0)
                            continue;
                    }
                    int relative = _vmm.MemReadValue<int>(pid, gSessionPtr + 3);
                    ulong gSessionGlobalSlots = gSessionPtr + 7 + (ulong)relative;
                    ulong userSessionState = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        userSessionState = _vmm.MemReadValue<ulong>(pid, _vmm.MemReadValue<ulong>(pid, _vmm.MemReadValue<ulong>(pid, gSessionGlobalSlots) + (ulong)(8 * i)));
                        if (userSessionState.IsValidKernelVA())
                            break;
                    }

                    if (!_vmm.Map_GetModuleFromName(pid, "win32kbase.sys", out var win32kbaseModule))
                        continue;
                    ulong win32kbaseBase = win32kbaseModule.vaBase;
                    ulong win32kbaseSize = win32kbaseModule.cbImageSize;

                    ulong ptr = _vmm.FindSignature(pid, "48 8D 90 ?? ?? ?? ?? E8 ?? ?? ?? ?? 0F 57 C0", win32kbaseBase, win32kbaseBase + win32kbaseSize);
                    if (ptr == 0)
                        continue;
                    uint sessionOffset = _vmm.MemReadValue<uint>(pid, ptr + 3);
                    ulong candidate = userSessionState + sessionOffset;

                    if (candidate.IsValidKernelVA())
                    {
                        _log?.Invoke($"Win11: resolved via csrss PID {pid}, session offset 0x{sessionOffset:X}");
                        ResolutionMethod = "Win11-SessionPointer";
                        result = candidate;
                        return true;
                    }
                }
                catch
                {
                    // Try next csrss PID
                }
            }

            _log?.Invoke("Win11: all csrss PIDs exhausted without finding gafAsyncKeyState");
            return false;
        }

        #endregion

        #region Win10 Resolution

        private bool TryResolveWin10KeyState(out ulong result)
        {
            result = 0;
            uint kernelPid = _winLogonPid | Vmm.PID_PROCESS_WITH_KERNELMEMORY;

            // Method 1: EAT export lookup — fastest, works on most Win10 builds
            try
            {
                _log?.Invoke("Win10: attempting EAT export lookup for gafAsyncKeyState...");
                var eatEntries = _vmm.Map_GetEAT(kernelPid, "win32kbase.sys", out _);
                if (eatEntries != null)
                {
                    foreach (var entry in eatEntries)
                    {
                        if (entry.sFunction == "gafAsyncKeyState" && entry.vaFunction.IsValidKernelVA())
                        {
                            _log?.Invoke($"Win10: resolved via EAT @ 0x{entry.vaFunction:X}");
                            ResolutionMethod = "Win10-EAT";
                            result = entry.vaFunction;
                            return true;
                        }
                    }
                }
                _log?.Invoke("Win10: EAT lookup found no matching export");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Win10: EAT lookup failed: {ex.Message}");
            }

            // Method 2: PDB symbol resolution — works when EAT is stripped/unavailable
            try
            {
                _log?.Invoke("Win10: attempting PDB symbol resolution...");
                ulong win32kbaseBase = 0;
                if (_vmm.Map_GetModuleFromName(kernelPid, "win32kbase.sys", out var pdbMod))
                    win32kbaseBase = pdbMod.vaBase;

                if (win32kbaseBase != 0 && _vmm.PdbLoad(kernelPid, win32kbaseBase, out _))
                {
                    if (_vmm.PdbSymbolAddress("win32kbase", "gafAsyncKeyState", out ulong pdbAddr) && pdbAddr.IsValidKernelVA())
                    {
                        _log?.Invoke($"Win10: resolved via PDB @ 0x{pdbAddr:X}");
                        ResolutionMethod = "Win10-PDB";
                        result = pdbAddr;
                        return true;
                    }
                }
                _log?.Invoke("Win10: PDB symbol resolution did not find gafAsyncKeyState");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Win10: PDB resolution failed: {ex.Message}");
            }

            // Method 3: Signature scan — broadest coverage across builds
            try
            {
                return TryResolveWin10KeyStateScan(kernelPid, out result);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Win10: signature scan failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Signature-based gafAsyncKeyState resolution for Win10.
        /// Multiple patterns are tried to cover builds from 1507 through 22H2.
        /// </summary>
        private bool TryResolveWin10KeyStateScan(uint kernelPid, out ulong result)
        {
            result = 0;
            ulong baseStart = 0, baseEnd = 0;
            uint scanPid = kernelPid;

            _log?.Invoke("Win10: attempting signature scan...");

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
            {
                _log?.Invoke("Win10: could not locate win32kbase.sys module");
                return false;
            }

            _log?.Invoke($"Win10: scanning win32kbase.sys @ 0x{baseStart:X} size 0x{baseEnd - baseStart:X}");

            // Signatures covering various Win10 builds:
            // Pattern 1: Win10 1903+ (19H1) - most common
            // Pattern 2: Win10 1809 and earlier - offset variant
            // Pattern 3: Win10 1507-1709 - indirect pointer table
            // Pattern 4: Win10 20H1+ variant with different suffix
            // Pattern 5: Win10 LTSC/Server variants
            ReadOnlySpan<string> signatures =
            [
                "48 8B 05 ?? ?? ?? ?? 48 63 D1 0F B6 44 10 08",
                "48 8B 05 ?? ?? ?? ?? 48 63 D1 0F B6 44 10 00",
                "48 8B 05 ?? ?? ?? ?? 48 8B 04 C8 0F B6 04 10 83 E0 01",
                "48 8B 05 ?? ?? ?? ?? 48 63 D1 44 0F B6 04 10",
                "48 8B 05 ?? ?? ?? ?? 48 63 CA 0F B6 44 08 08",
            ];

            foreach (var sig in signatures)
            {
                ulong ptr = _vmm.FindSignature(scanPid, sig, baseStart, baseEnd);
                if (ptr == 0)
                    continue;

                int relative = _vmm.MemReadValue<int>(scanPid, ptr + 3);
                if (relative == 0)
                    continue;

                ulong candidate = ptr + 7 + (ulong)relative;
                if (candidate.IsValidKernelVA())
                {
                    _log?.Invoke($"Win10: resolved via sig scan @ 0x{candidate:X} (pattern: {sig[..20]}...)");
                    ResolutionMethod = "Win10-SigScan";
                    result = candidate;
                    return true;
                }
            }

            _log?.Invoke("Win10: all signature patterns exhausted");
            return false;
        }

        #endregion

        /// <summary>
        /// Reads the Windows build number from the target machine's registry
        /// via DMA. Falls back to the local host registry if the DMA read fails.
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
    }
}
