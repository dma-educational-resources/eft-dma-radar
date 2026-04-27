#pragma warning disable IDE0130

using ArenaUtils = eft_dma_radar.Arena.Misc.Utils;
using UTF8String = eft_dma_radar.Arena.Misc.UTF8String;

namespace eft_dma_radar.Arena.Unity.IL2CPP
{
    public static partial class Il2CppDumper
    {
        // ── Constants ────────────────────────────────────────────────────────

        private const int MaxTableEntries = 64;
        private const int MaxLastResortEntries = 1024;
        private const int EarlyProbeCount  = 16;
        private const int EarlyProbeRequired = 8;
        private const int MidProbeOffset   = 5_000;
        private const int MidProbeCount    = 8;
        private const int MidProbeRequired = 3;
        private const string GameAssemblyName = "GameAssembly.dll";
        private const string LogTag = "[Il2CppDumper]";

        // ── Signatures ───────────────────────────────────────────────────────

        private static readonly (string Sig, int RelOffset, int InstrLen, string Desc)[] TypeInfoTableSigs =
        [
            // Primary: long unique read-path through sub_18065BF60; very robust across patches
            ("48 8B 05 ? ? ? ? ? ? ? ? ? ? ? 90 48 85 DB 75 ? 48 8D 2D ? ? ? ? 48 89 6C 24 ? 48 8B CD E8 ? ? ? ? 90 ? ? ? 48 85 DB 75 ? 8B CF", 3, 7, "read: mov rax,[rip+rel32] (table lookup)"),
            // Fallback A: index-lookup read at 0x18065d8df — extended with movsxd+test to reduce false-positive risk
            ("48 8B 0D ? ? ? ? 48 63 D0 ? ? ? ? 48 85 C9 74", 3, 7, "read: mov rcx,[rip+rel32]; movsxd+test (index lookup)"),
            // Fallback B: full-init write at 0x18065a0ba — trailing E8 removed so a call-target relocation won't break it
            ("48 89 05 ? ? ? ? 4C 8B 05 ? ? ? ? 48 8B 05 ? ? ? ? 48 63 48 ? BA ? ? ? ? 41 FF D0 48 89 05 ? ? ? ?", 3, 7, "write: mov [rip+rel32],rax; mov r8,[rip+rel32] (full init)"),
        ];

        // ── Last-resort signatures (only used when all primary sigs fail) ────
        // These patterns are too broad for normal use (>64 matches) but can still
        // find the correct RVA via ValidateTypeInfoTable when nothing else works.

        private static readonly (string Sig, int RelOffset, int InstrLen, string Desc)[] LastResortTableSigs =
        [
            ("48 89 05 ? ? ? ?", 3, 7, "last-resort: any mov [rip+rel32],rax (wide write)"),
        ];

        // ── Resolution pipeline ──────────────────────────────────────────────

        private static bool ResolveTypeInfoTableRva(ulong gaBase, bool quiet = false)
        {
            var testedRvas = new HashSet<ulong>();
            var sigResults = new List<SigScanResult>(TypeInfoTableSigs.Length);
            (ulong rva, ulong sigAddr, string sig)? first = null;

            for (int i = 0; i < TypeInfoTableSigs.Length; i++)
            {
                var (sig, relOff, instrLen, desc) = TypeInfoTableSigs[i];
                var (result, scanResult) = TryResolveFromSignature(i, sig, relOff, instrLen, desc, gaBase, testedRvas);
                sigResults.Add(scanResult);
                if (result.HasValue && first is null)
                    first = result;
            }

            // Last-resort pass — only runs when every primary sig produced nothing
            if (first is null)
            {
                if (!quiet) Log.WriteLine($"{LogTag} Primary sigs exhausted, trying last-resort signatures...");
                for (int i = 0; i < LastResortTableSigs.Length; i++)
                {
                    var (sig, relOff, instrLen, desc) = LastResortTableSigs[i];
                    var (result, scanResult) = TryResolveFromSignature(
                        TypeInfoTableSigs.Length + i, sig, relOff, instrLen, desc, gaBase, testedRvas,
                        MaxLastResortEntries);
                    sigResults.Add(scanResult);
                    if (result.HasValue && first is null)
                        first = result;
                }
            }

            bool success;
            if (first.HasValue)
            {
                var prev = SDK.Offsets.Special.TypeInfoTableRva;
                SDK.Offsets.Special.TypeInfoTableRva = first.Value.rva;
                Log.WriteLine($"{LogTag} TypeInfoTable resolved: rva=0x{first.Value.rva:X}");
                if (prev != first.Value.rva)
                    Log.WriteLine($"{LogTag} TypeInfoTableRva UPDATED: 0x{prev:X} → 0x{first.Value.rva:X}");
                _lastResolutionMode = "signature";
                success = true;
            }
            else if (SDK.Offsets.Special.TypeInfoTableRva != 0 && ValidateTypeInfoTable(gaBase, SDK.Offsets.Special.TypeInfoTableRva))
            {
                Log.WriteLine($"{LogTag} TypeInfoTable using fallback RVA: 0x{SDK.Offsets.Special.TypeInfoTableRva:X}");
                _lastResolutionMode = "fallback (hardcoded)";
                success = true;
            }
            else
            {
                if (!quiet) Log.WriteLine($"{LogTag} WARNING: All TypeInfoTable resolution strategies failed!");
                _lastResolutionMode = "FAILED";
                success = false;
            }

            _lastSigResults = [.. sigResults];
            return success;
        }

        private static ((ulong rva, ulong sigAddr, string sig)?, SigScanResult) TryResolveFromSignature(
            int index, string sig, int relOff, int instrLen, string desc, ulong gaBase, HashSet<ulong> testedRvas,
            int maxEntries = MaxTableEntries)
        {
            ulong[] sigAddrs;
            try { sigAddrs = Memory.FindSignatures(sig, GameAssemblyName, maxEntries); }
            catch (Exception ex)
            {
                Log.WriteLine($"{LogTag} TypeInfoTable sig[{index}] scan error: {ex.Message}");
                return (null, new SigScanResult(index, desc, "ERROR", 0, 0, 0));
            }

            if (sigAddrs.Length == 0)
                return (null, new SigScanResult(index, desc, "MISS", 0, 0, 0));

            ulong duplicateRva = 0;
            int validCount = 0;
            foreach (var sigAddr in sigAddrs)
            {
                var rva = ResolveRipRelativeRva(sigAddr, relOff, instrLen, gaBase);
                if (rva == 0 || !ValidateTypeInfoTable(gaBase, rva)) continue;
                validCount++;
                if (testedRvas.Add(rva))
                    return ((rva, sigAddr, sig), new SigScanResult(index, desc, "OK", sigAddrs.Length, validCount, rva));
                duplicateRva = rva;
            }

            if (duplicateRva != 0)
                return (null, new SigScanResult(index, desc, "DUPLICATE", sigAddrs.Length, validCount, duplicateRva));

            return (null, new SigScanResult(index, desc, "INVALID", sigAddrs.Length, validCount, 0));
        }

        // ── RIP-relative decode ──────────────────────────────────────────────

        private static ulong ResolveRipRelativeRva(ulong sigAddr, int relOffset, int instrLen, ulong gaBase)
        {
            int rel;
            try { rel = Memory.ReadValue<int>(sigAddr + (ulong)relOffset, false); }
            catch { return 0; }
            ulong globalVa = sigAddr + (ulong)instrLen + (ulong)(long)rel;
            return globalVa > gaBase ? globalVa - gaBase : 0;
        }

        // ── TypeInfoTable validation ─────────────────────────────────────────

        private static bool ValidateTypeInfoTable(ulong gaBase, ulong rva)
        {
            ulong tablePtr;
            try { tablePtr = Memory.ReadPtr(gaBase + rva, false); }
            catch { return false; }
            return ArenaUtils.IsValidVirtualAddress(tablePtr)
                && ProbeTableEntries(tablePtr, 0, EarlyProbeCount, EarlyProbeRequired)
                && ProbeTableEntries(tablePtr, MidProbeOffset, MidProbeCount, MidProbeRequired);
        }

        private static bool ProbeTableEntries(ulong tablePtr, int startIndex, int count, int required)
        {
            ulong[] ptrs;
            try { ptrs = Memory.ReadArray<ulong>(tablePtr + (ulong)startIndex * 8, count, false); }
            catch { return false; }
            int valid = 0;
            foreach (var ptr in ptrs)
                if (IsValidClassPtr(ptr) && ++valid >= required)
                    return true;
            return false;
        }

        private static bool IsValidClassPtr(ulong ptr)
        {
            if (!ArenaUtils.IsValidVirtualAddress(ptr)) return false;
            try
            {
                var namePtr = Memory.ReadValue<ulong>(ptr + K_Name, false);
                if (!ArenaUtils.IsValidVirtualAddress(namePtr)) return false;
                var name = ReadStr(namePtr);
                return !string.IsNullOrEmpty(name) && name.Length < MaxNameLen && IsPlausibleClassName(name);
            }
            catch { return false; }
        }

        private static bool IsPlausibleClassName(string name)
        {
            foreach (char c in name)
                if (c < 0x20 || (c > 0x7E && c < 0xA0)) return false;
            return true;
        }

        internal static ulong ResolveKlassByTypeIndex(uint typeIndex)
        {
            if (typeIndex == 0) return 0;
            var gaBase = Memory.GameAssemblyBase;
            if (!ArenaUtils.IsValidVirtualAddress(gaBase) || SDK.Offsets.Special.TypeInfoTableRva == 0)
                return 0;
            if (!Memory.TryReadPtr(gaBase + SDK.Offsets.Special.TypeInfoTableRva, out var tablePtr, false)
                || !ArenaUtils.IsValidVirtualAddress(tablePtr))
                return 0;
            return Memory.TryReadValue<ulong>(tablePtr + (ulong)typeIndex * 8, out var ptr, false)
                && ArenaUtils.IsValidVirtualAddress(ptr) ? ptr : 0;
        }
    }
}
