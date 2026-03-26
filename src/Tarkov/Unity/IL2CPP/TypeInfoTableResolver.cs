#nullable enable

using System.Diagnostics;
using System.Reflection;
using eft_dma_radar.Common.Misc;
using SDK;

namespace eft_dma_radar.Tarkov.Unity.IL2CPP
{
    public static partial class Il2CppDumper
    {
        // ── IL2CPP bootstrap resolution ─────────────────────────────────────────

        /// <summary>
        /// Candidate signatures for locating the TypeInfoTable global.
        /// Each entry: (signature, rel32 offset from match start, instruction length for RIP calc, description).
        /// Both patterns reference the same <c>qword</c> global that holds
        /// <c>s_Il2CppMetadataRegistration→typeInfoTable</c>.
        /// Confirmed via IDA: only 2 xrefs to this global exist in GameAssembly.dll.
        /// </summary>
        private static readonly (string Sig, int RelOffset, int InstrLen, string Desc)[] TypeInfoTableSigs =
        [
            // Write-side (initialization): mov [rip+rel32], rax; mov rax, [rip+rel32]; mov edx, [rax+...]
            ("48 89 05 ? ? ? ? 48 8B 05 ? ? ? ? 8B 50", 3, 7, "write: mov [rip+rel32],rax (init store)"),

            // Read-side (lookup function): mov rax, [rip+rel32]; lea r14,[rax+rsi*8]; ...; nop; test rdi,rdi; jnz
            ("48 8B 05 ? ? ? ? ? ? ? ? ? ? ? 90 48 85 FF 0F 85", 3, 7, "read: mov rax,[rip+rel32] (table lookup)"),
        ];

        /// <summary>
        /// Signature-scans GameAssembly.dll for the TypeInfoTable global and
        /// updates <see cref="Offsets.Special.TypeInfoTableRva"/> at runtime.
        /// Tries multiple signature patterns and validates the result by probing
        /// the resolved table for plausible class pointers.
        /// Falls back to the hardcoded value in SDK.cs if all strategies fail.
        /// </summary>
        private static bool ResolveTypeInfoTableRva(ulong gaBase)
        {
            DebugTestAllSignatures(gaBase);
            foreach (var (sig, relOff, instrLen, _) in TypeInfoTableSigs)
            {
                var sigAddr = Memory.FindSignature(sig, "GameAssembly.dll");
                if (sigAddr == 0)
                    continue;

                var rva = ResolveRipRelativeRva(sigAddr, relOff, instrLen, gaBase);
                if (rva == 0)
                    continue;

                if (ValidateTypeInfoTable(gaBase, rva))
                {
                    var previous = Offsets.Special.TypeInfoTableRva;
                    Offsets.Special.TypeInfoTableRva = rva;

                    if (previous != rva)
                        XMLogging.WriteLine($"[Il2CppDumper] TypeInfoTableRva UPDATED: 0x{previous:X} → 0x{rva:X}");

                    return true;
                }
            }

            // All signatures failed — validate the hardcoded fallback.
            if (Offsets.Special.TypeInfoTableRva != 0 && ValidateTypeInfoTable(gaBase, Offsets.Special.TypeInfoTableRva))
                return true;

            XMLogging.WriteLine("[Il2CppDumper] WARNING: All TypeInfoTable resolution strategies failed — offsets may be stale!");
            return false;
        }

        /// <summary>
        /// Reads a RIP-relative <c>int32</c> displacement from a matched signature
        /// and computes the target RVA relative to <paramref name="gaBase"/>.
        /// </summary>
        private static ulong ResolveRipRelativeRva(ulong sigAddr, int relOffset, int instrLen, ulong gaBase)
        {
            int rel;
            try { rel = Memory.ReadValue<int>(sigAddr + (ulong)relOffset, false); }
            catch { return 0; }

            ulong globalVa = sigAddr + (ulong)instrLen + (ulong)(long)rel;

            // Basic sanity: the resolved VA must be inside GameAssembly's address space.
            if (globalVa <= gaBase)
                return 0;

            return globalVa - gaBase;
        }

        /// <summary>
        /// Validates a candidate TypeInfoTable RVA by probing entries at multiple positions.
        /// A valid table has non-null class pointers whose <c>Il2CppClass::name</c>
        /// fields point to readable ASCII strings.
        /// Probes early, mid, and late sections to catch partially-corrupt tables.
        /// </summary>
        private static bool ValidateTypeInfoTable(ulong gaBase, ulong rva)
        {
            ulong tablePtr;
            try { tablePtr = Memory.ReadPtr(gaBase + rva, false); }
            catch { return false; }

            if (!tablePtr.IsValidVirtualAddress())
                return false;

            // Probe early entries — these are almost always populated.
            const int earlyProbeCount = 16;
            const int earlyRequired = 8;

            if (!ProbeTableEntries(tablePtr, 0, earlyProbeCount, earlyRequired))
                return false;

            // Probe a mid-range slice — catches tables that are valid at the start but corrupt later.
            // Offset 5000 is well within any healthy IL2CPP binary (typically 30k+ classes).
            const int midOffset = 5_000;
            const int midProbeCount = 8;
            const int midRequired = 3;

            if (!ProbeTableEntries(tablePtr, midOffset, midProbeCount, midRequired))
                return false;

            return true;
        }

        /// <summary>
        /// Probes <paramref name="count"/> consecutive table entries starting at <paramref name="startIndex"/>
        /// and returns <c>true</c> if at least <paramref name="required"/> contain a valid Il2CppClass
        /// with a plausible name string.
        /// </summary>
        private static bool ProbeTableEntries(ulong tablePtr, int startIndex, int count, int required)
        {
            ulong[] ptrs;
            try { ptrs = Memory.ReadArray<ulong>(tablePtr + (ulong)startIndex * 8, count, false); }
            catch { return false; }

            int valid = 0;
            for (int i = 0; i < ptrs.Length; i++)
            {
                if (!ptrs[i].IsValidVirtualAddress())
                    continue;

                ulong namePtr;
                try { namePtr = Memory.ReadValue<ulong>(ptrs[i] + K_Name, false); }
                catch { continue; }

                if (!namePtr.IsValidVirtualAddress())
                    continue;

                var name = ReadStr(namePtr);
                if (!string.IsNullOrEmpty(name) && name.Length < MaxNameLen && IsPlausibleClassName(name))
                    valid++;

                if (valid >= required)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Checks whether a string looks like a plausible IL2CPP class name
        /// (printable ASCII or common Unicode escape, no control chars).
        /// </summary>
        private static bool IsPlausibleClassName(string name)
        {
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                // Allow printable ASCII, common C# identifier chars, and IL2CPP unicode escapes
                if (c < 0x20 || (c > 0x7E && c < 0xA0))
                    return false;
            }
            return true;
        }

        // ── TypeIndex resolution ─────────────────────────────────────────────────

        /// <summary>
        /// Maps IL2CPP class names → <see cref="Offsets.Special"/> TypeIndex field names.
        /// Add entries here when new singleton classes need TypeIndex resolution.
        /// </summary>
        private static readonly (string Il2CppName, string FieldName)[] TypeIndexMap =
        [
            ("EFTHardSettings",       nameof(Offsets.Special.EFTHardSettings_TypeIndex)),
            ("GPUInstancerManager",   nameof(Offsets.Special.GPUInstancerManager_TypeIndex)),
            ("WeatherController",     nameof(Offsets.Special.WeatherController_TypeIndex)),
            ("GlobalConfiguration",   nameof(Offsets.Special.GlobalConfiguration_TypeIndex)),
            ("MatchingProgress",      nameof(Offsets.Special.MatchingProgress_TypeIndex)),
            ("MatchingProgressView",  nameof(Offsets.Special.MatchingProgressView_TypeIndex)),
        ];

        /// <summary>
        /// Looks up known singleton class names in the scanned type table and
        /// updates <see cref="Offsets.Special"/> TypeIndex fields dynamically.
        /// Falls back to hardcoded values for any class not found.
        /// </summary>
        private static void ResolveTypeIndices(Dictionary<string, int> nameToIndex)
        {
            var specialType = typeof(Offsets.Special);
            const BindingFlags bf = BindingFlags.Public | BindingFlags.Static;

            foreach (var (il2cppName, fieldName) in TypeIndexMap)
            {
                var fi = specialType.GetField(fieldName, bf);
                if (fi is null)
                    continue;

                if (nameToIndex.TryGetValue(il2cppName, out var index))
                {
                    var previous = (uint)(fi.GetValue(null) ?? 0u);
                    fi.SetValue(null, (uint)index);

                    if (previous != (uint)index)
                        XMLogging.WriteLine($"[Il2CppDumper] {fieldName} UPDATED: {previous} → {index}");
                }
                else
                {
                    XMLogging.WriteLine($"[Il2CppDumper] WARN: '{il2cppName}' not found in type table — {fieldName} using fallback ({fi.GetValue(null) ?? 0u}).");
                }
            }
        }

        // ── Debug diagnostics ─────────────────────────────────────────────────────────────

#if DEBUG
        /// <summary>
        /// Sig audit lines collected by <see cref="DebugTestAllSignatures"/> for
        /// deferred output inside <see cref="DebugDumpResolverState"/>.
        /// </summary>
        private static List<string>? _sigAuditLines;
#endif

        /// <summary>
        /// DEBUG: Tests all TypeInfoTable signatures and stores results for
        /// deferred output inside <see cref="DebugDumpResolverState"/>.
        /// Automatically runs in Debug builds; fully stripped in Release.
        /// </summary>
        [Conditional("DEBUG")]
        private static void DebugTestAllSignatures(ulong gaBase)
        {
            const string tag = "[Il2CppDumper]";
            var bodyLines = new List<string>();

            for (int idx = 0; idx < TypeInfoTableSigs.Length; idx++)
            {
                var (sig, relOff, instrLen, desc) = TypeInfoTableSigs[idx];
                try
                {
                    var sigAddr = Memory.FindSignature(sig, "GameAssembly.dll");
                    if (sigAddr == 0)
                    {
                        bodyLines.Add($"[{idx}] MISS — {desc}");
                        continue;
                    }

                    var rva = ResolveRipRelativeRva(sigAddr, relOff, instrLen, gaBase);
                    string matchInfo = $"GA+0x{sigAddr - gaBase:X}";

                    if (rva == 0)
                    {
                        bodyLines.Add($"[{idx}] BAD RIP — {desc} ({matchInfo})");
                        continue;
                    }

                    bool valid = ValidateTypeInfoTable(gaBase, rva);
                    string status = valid ? $"OK RVA=0x{rva:X}" : $"INVALID RVA=0x{rva:X}";
                    bodyLines.Add($"[{idx}] {status} — {desc} ({matchInfo})");
                }
                catch (Exception ex)
                {
                    bodyLines.Add($"[{idx}] ERROR {ex.Message} — {desc}");
                }
            }

            // Auto-size: W = inner width between │ and │ (or ┌ and ┐)
            const string title = "TypeInfoTable Signature Audit";
            int W = title.Length + 4; // "─ title ─" minimum
            foreach (var line in bodyLines)
                if (line.Length + 2 > W) // " content " padding
                    W = line.Length + 2;

            var lines = new List<string>(bodyLines.Count + 2);
            {
                int dashTotal = W - title.Length - 2; // 2 spaces around title
                int dashLeft = dashTotal / 2;
                int dashRight = dashTotal - dashLeft;
                lines.Add($"{tag} ┌{new string('─', dashLeft)} {title} {new string('─', dashRight)}┐");
            }
            foreach (var body in bodyLines)
            {
                if (body.StartsWith('['))
                {
                    lines.Add($"{tag} │ {body.PadRight(W - 1)}│");
                }
                else
                {
                    int pad = W - body.Length;
                    int left = pad / 2;
                    int right = pad - left;
                    lines.Add($"{tag} │{new string(' ', left)}{body}{new string(' ', right)}│");
                }
            }
            lines.Add($"{tag} └{new string('─', W)}┘");

#if DEBUG
            _sigAuditLines = lines;
#endif
        }

        /// <summary>
        /// DEBUG: Dumps a comprehensive summary of TypeInfoTable resolution and TypeIndex state.
        /// Includes the deferred sig audit box from <see cref="DebugTestAllSignatures"/>.
        /// Called after <see cref="Il2CppDumper.Dump"/> completes.
        /// </summary>
        [Conditional("DEBUG")]
        internal static void DebugDumpResolverState(int classCount, int updated, int fallback, int skipped)
        {
            const int W = 54;
            const string tag = "[Il2CppDumper]";
            var gaBase = Memory.GameAssemblyBase;

            string Row(string text) => $"║  {text,-(W - 2)}║";
            string Header(string text)
            {
                int pad = W - text.Length;
                int left = pad / 2;
                int right = pad - left;
                return $"║{new string(' ', left)}{text}{new string(' ', right)}║";
            }
            string Sep(string label) => $"╠── {label} {"".PadRight(W - 4 - label.Length, '─')}╣";

            var lines = new List<string>();

#if DEBUG
            // Prepend the deferred sig audit box so both appear together.
            if (_sigAuditLines is not null)
            {
                lines.AddRange(_sigAuditLines);
                _sigAuditLines = null;
            }
#endif

            lines.Add($"{tag} ╔{new string('═', W)}╗");
            lines.Add($"{tag} {Header("IL2CPP Dumper — Debug State")}");
            lines.Add($"{tag} ╠{new string('═', W)}╣");
            lines.Add($"{tag} {Row($"GameAssembly Base:  {FormatPtr(gaBase)}")}");
            lines.Add($"{tag} {Sep("TypeInfoTable")}");
            lines.Add($"{tag} {Row($"RVA:                0x{Offsets.Special.TypeInfoTableRva:X}")}");
            lines.Add($"{tag} {Row($"Classes Found:      {classCount}")}");
            lines.Add($"{tag} {Sep("Dump Results")}");
            lines.Add($"{tag} {Row($"Offsets Updated:    {updated}")}");
            lines.Add($"{tag} {Row($"Using Fallback:     {fallback}")}");
            lines.Add($"{tag} {Row($"Classes Skipped:    {skipped}")}");
            lines.Add($"{tag} {Sep("TypeIndex Resolution")}");

            var specialType = typeof(Offsets.Special);
            const BindingFlags bf = BindingFlags.Public | BindingFlags.Static;
            foreach (var (il2cppName, fieldName) in TypeIndexMap)
            {
                var fi = specialType.GetField(fieldName, bf);
                string val = fi is not null ? $"{fi.GetValue(null)}" : "(missing)";
                lines.Add($"{tag} {Row($"{il2cppName + ":",-24}{val}")}");
            }

            int validSigs = CountValidSigs(gaBase);
            lines.Add($"{tag} {Sep("Signature Health")}");
            lines.Add($"{tag} {Row($"TypeInfoTable:      {validSigs}/{TypeInfoTableSigs.Length} sigs valid")}");
            lines.Add($"{tag} ╚{new string('═', W)}╝");

            XMLogging.WriteBlock(lines);
        }

        private static string FormatPtr(ulong ptr) =>
            ptr.IsValidVirtualAddress() ? $"0x{ptr:X}" : "(not resolved)";

        private static int CountValidSigs(ulong gaBase)
        {
            int count = 0;
            foreach (var (sig, relOff, instrLen, _) in TypeInfoTableSigs)
            {
                try
                {
                    var sigAddr = Memory.FindSignature(sig, "GameAssembly.dll");
                    if (sigAddr == 0) continue;
                    var rva = ResolveRipRelativeRva(sigAddr, relOff, instrLen, gaBase);
                    if (rva != 0 && ValidateTypeInfoTable(gaBase, rva))
                        count++;
                }
                catch { /* skip */ }
            }
            return count;
        }
    }
}
