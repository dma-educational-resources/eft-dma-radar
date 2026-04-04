/*  
 *  VmmSharpEx by Lone (Lone DMA)
 *  Copyright (C) 2025 AGPL-3.0
*/

using System.Runtime.CompilerServices;
using System.Text;
using VmmSharpEx.Options;

namespace VmmSharpEx.Extensions
{
    /// <summary>
    /// Contains various extension methods to implement additional functionality with Vmm and/or Memory Operations.
    /// </summary>
    public static class VmmExtensions
    {
        /// <summary>
        /// Calculates a new address by adding the instruction size and a relative virtual address (RVA) to the current address.
        /// </summary>
        /// <param name="address">Current virtual address.</param>
        /// <param name="instructionSize">Assembly instruction length in bytes.</param>
        /// <param name="rva">Relative virtual address (RVA).</param>
        /// <returns>New address calculated from the RVA.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong AddRVA(this ulong address, uint instructionSize, int rva)
        {
            // signed rva can be negative, so we need to use long for the calculation
            long result = (long)address + instructionSize + rva;
            return (ulong)result;
        }

        /// <summary>
        /// Calculates a new address by adding the instruction size and a relative virtual address (RVA) to the current address.
        /// </summary>
        /// <param name="address">Current virtual address.</param>
        /// <param name="instructionSize">Assembly instruction length in bytes.</param>
        /// <param name="rva">Relative virtual address (RVA).</param>
        /// <returns>New address calculated from the RVA.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong AddRVA(this ulong address, uint instructionSize, uint rva)
        {
            return address + instructionSize + rva;
        }

        /// <summary>
        /// Checks if the given virtual address is valid within win-x64 architecture.
        /// </summary>
        /// <param name="va">Virtual address to validate.</param>
        /// <returns><see langword="true"/> if valid; otherwise <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidVA(this ulong va) =>
            VmmUtilities.IsValidVA(va);

        /// <summary>
        /// Checks if the given virtual address is a valid usermode address within win-x64 architecture.
        /// </summary>
        /// <param name="va">Virtual address to validate.</param>
        /// <returns><see langword="true"/> if valid; otherwise <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidUserVA(this ulong va) =>
            VmmUtilities.IsValidUserVA(va);

        /// <summary>
        /// Checks if the given virtual address is a valid kernel address within win-x64 architecture.
        /// </summary>
        /// <param name="va">Virtual address to validate.</param>
        /// <returns><see langword="true"/> if valid; otherwise <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidKernelVA(this ulong va) =>
            VmmUtilities.IsValidKernelVA(va);

        /// <summary>
        /// Throws a <see cref="VmmException"/> if the given virtual address is not valid within win-x64 architecture.
        /// </summary>
        /// <param name="va">Virtual address to validate.</param>
        /// <param name="paramName">Parameter name for the exception message.</param>
        /// <exception cref="VmmException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfInvalidVA(this ulong va, string? paramName = null)
        {
            if (!VmmUtilities.IsValidVA(va))
                throw new VmmException(paramName is null ?
                    $"Address 0x{va:X} is not a valid x64 virtual address!" :
                    $"'{paramName}' Address 0x{va:X} is not a valid x64 virtual address!");
        }

        /// <summary>
        /// Throws a <see cref="VmmException"/> if the given virtual address is not a valid usermode address within win-x64 architecture.
        /// </summary>
        /// <param name="va">Virtual address to validate.</param>
        /// <param name="paramName">Parameter name for the exception message.</param>
        /// <exception cref="VmmException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfInvalidUserVA(this ulong va, string? paramName = null)
        {
            if (!VmmUtilities.IsValidUserVA(va))
                throw new VmmException(paramName is null ?
                    $"Address 0x{va:X} is not a valid x64 user virtual address!" :
                    $"'{paramName}' Address 0x{va:X} is not a valid x64 user virtual address!");
        }

        /// <summary>
        /// Throws a <see cref="VmmException"/> if the given virtual address is not a valid kernel address within win-x64 architecture.
        /// </summary>
        /// <param name="va">Virtual address to validate.</param>
        /// <param name="paramName">Parameter name for the exception message.</param>
        /// <exception cref="VmmException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfInvalidKernelVA(this ulong va, string? paramName = null)
        {
            if (!VmmUtilities.IsValidKernelVA(va))
                throw new VmmException(paramName is null ?
                    $"Address 0x{va:X} is not a valid x64 kernel virtual address!" :
                    $"'{paramName}' Address 0x{va:X} is not a valid x64 kernel virtual address!");
        }

        /// <summary>
        /// This fixes cr3 database shuffling.
        /// It fixes it by iterating over all DTB's that exist within your system and looks for specific ones
        /// that nolonger have a PID assigned to them, aka their pid is 0
        /// it then puts it in a vector to later try each possible DTB to find the DTB of the process.
        /// NOTE: Using FixCR3 requires you to have symsrv.dll, dbghelp.dll and info.db
        /// CREDIT: Contributed by Mambo, but based off Metick's DMA Lib https://github.com/Metick/DMALibrary :)
        /// </summary>
        /// <param name="vmm">Vmm instance.</param>
        /// <param name="processName">Process name to fix.</param>
        /// <param name="pid">PID of process to fix.</param>
        /// <returns>TRUE if successful, otherwise FALSE.</returns>
        public static bool FixCr3(this Vmm vmm, string processName, uint pid)
        {
            // If already mapped successfully, skip
            if (vmm.Map_GetModuleFromName(pid, processName, out var mod) && mod.fValid)
            {
                return true;
            }
            // Ensure plugins are ready
            if (!vmm.InitializePlugins())
            {
                return false;
            }

            Thread.Sleep(500); // Let plugin init finish

            // Wait for progress to reach 100%
            while (true)
            {
                _ = vmm.VfsRead(@"\misc\procinfo\progress_percent.txt", out var percentBytes, 4);
                if (percentBytes is not null &&
                    int.TryParse(Encoding.ASCII.GetString(percentBytes).Trim(), out int percent) &&
                    percent == 100)
                    break;

                Thread.Sleep(100);
            }

            // VFS list and read DTBs
            _ = vmm.VfsList(@"\misc\procinfo\");
            _ = vmm.VfsRead(@"\misc\procinfo\dtb.txt", out var dtbRaw, 0x1000);
            if (dtbRaw is null)
                return false;

            var possibleDtbs = new List<ulong>();
            var lines = Encoding.ASCII.GetString(dtbRaw)
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 6)
                    continue;

                try
                {
                    uint parsedPid = uint.Parse(tokens[1]);
                    ulong dtb = Convert.ToUInt64(tokens[2], 16);
                    string name = tokens[5];

                    if (parsedPid == 0 || processName.Contains(name))
                        possibleDtbs.Add(dtb);
                }
                catch { }
            }

            foreach (var dtb in possibleDtbs)
            {
                vmm.ConfigSet((VmmOption)((ulong)VmmOption.PROCESS_DTB | pid), dtb);
                if (vmm.Map_GetModuleFromName(pid, processName, out mod) && mod.fValid)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Find a signature within a process' memory.
        /// </summary>
        /// <param name="vmm">Vmm instance.</param>
        /// <param name="pid">Process to search within.</param>
        /// <param name="signature">Signature to search for (max 32 bytes). Hex Characters (separated by space) with optional ?? wildcard mask. Ex: 0F 1F ?? ?? 90 AA</param>
        /// <param name="module">Module to search within. The search will be bounded within this module.</param>
        /// <returns>Address of first occurrence of signature, otherwise 0 if failed.</returns>
        public static ulong FindSignature(this Vmm vmm, uint pid, string signature, string module)
        {
            if (!vmm.Map_GetModuleFromName(pid, module, out var moduleInfo))
                throw new VmmException($"Failed to get module info for module '{module}'");
            return vmm.FindSignature(
                pid: pid,
                signature: signature,
                addrMin: moduleInfo.vaBase,
                addrMax: moduleInfo.vaBase + moduleInfo.cbImageSize);
        }

        /// <summary>
        /// Find a signature within a process' memory.
        /// </summary>
        /// <param name="vmm">Vmm instance.</param>
        /// <param name="pid">Process to search within.</param>
        /// <param name="signature">Signature to search for (max 32 bytes). Hex Characters (separated by space) with optional ?? wildcard mask. Ex: 0F 1F ?? ?? 90 AA</param>
        /// <param name="addrMin">(Optional) Minimum Address to begin scanning at. By default will scan whole process.</param>
        /// <param name="addrMax">(Optional) Maximum Address to end scanning at. By default will scan whole process.</param>
        /// <returns>Address of first occurrence of signature, otherwise 0 if failed.</returns>
        public static ulong FindSignature(this Vmm vmm, uint pid, string signature, ulong addrMin = 0, ulong addrMax = ulong.MaxValue)
        {
            ArgumentNullException.ThrowIfNull(vmm, nameof(vmm));
            ArgumentException.ThrowIfNullOrEmpty(signature, nameof(signature));
            string[] sigSplit = signature.Split(' ');
            ArgumentOutOfRangeException.ThrowIfGreaterThan(sigSplit.Length, 32, nameof(signature));
            byte[] searchBytes = new byte[sigSplit.Length];
            byte[] skipBytes = new byte[sigSplit.Length];
            for (int i = 0; i < sigSplit.Length; i++)
            {
                string byteStr = sigSplit[i];
                if (byteStr.StartsWith('?'))
                {
                    searchBytes[i] = 0;
                    skipBytes[i] = 0xff;
                }
                else
                {
                    searchBytes[i] = byte.Parse(byteStr, System.Globalization.NumberStyles.HexNumber);
                    skipBytes[i] = 0;
                }
            }
            var entries = new VmmSearch.SearchItem[]
            {
                new VmmSearch.SearchItem
                {
                    Search = searchBytes,
                    SkipMask = skipBytes
                }
            };
            var vmmSearch = vmm.MemSearch(
                pid: pid,
                searchItems: entries,
                addr_min: addrMin,
                addr_max: addrMax,
                cMaxResult: 1);
            if (vmmSearch.Results.Count == 0)
            {
                return 0;
            }
            return vmmSearch.Results.First().Address;
        }

        /// <summary>
        /// Find multiple signature matches within a module using chunked memory reads.
        /// </summary>
        /// <param name="vmm">The VMM instance.</param>
        /// <param name="pid">The process ID to scan.</param>
        /// <param name="signature">IDA-style byte pattern (e.g. "48 8B 05 ?? ?? ?? ??").</param>
        /// <param name="moduleName">The module name to scan within.</param>
        /// <param name="maxMatches">Maximum number of matches to return.</param>
        /// <returns>Array of virtual addresses where the pattern was found.</returns>
        public static ulong[] FindSignatures(this Vmm vmm, uint pid, string signature, string moduleName, int maxMatches = int.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(signature) || maxMatches <= 0)
                return [];
            if (!TryParseSignature(signature, out var pattern))
                return [];

            var moduleBase = vmm.ProcessGetModuleBase(pid, moduleName);
            if (moduleBase == 0 || moduleBase == ulong.MaxValue)
                return [];

            const ulong MAX_SEARCH_SIZE = 0xC800000;
            const ulong CHUNK_SIZE = 0x1000000;
            ulong rangeEnd = moduleBase + MAX_SEARCH_SIZE;
            int overlap = Math.Max(0x100, pattern.Length - 1);
            ulong step = CHUNK_SIZE > (ulong)overlap ? CHUNK_SIZE - (ulong)overlap : CHUNK_SIZE;
            var results = new List<ulong>(Math.Min(maxMatches, 64));

            for (ulong chunkStart = moduleBase; chunkStart < rangeEnd && results.Count < maxMatches; chunkStart += step)
            {
                ulong chunkEnd = Math.Min(chunkStart + CHUNK_SIZE, rangeEnd);
                var chunkMatches = FindSignaturesInRange(vmm, pid, pattern, chunkStart, chunkEnd, maxMatches - results.Count);
                foreach (var match in chunkMatches)
                {
                    if (results.Count == 0 || results[^1] != match) results.Add(match);
                    if (results.Count >= maxMatches) break;
                }
            }
            return [.. results];
        }

        private static ulong[] FindSignaturesInRange(Vmm vmm, uint pid, byte?[] pattern, ulong rangeStart, ulong rangeEnd, int maxMatches)
        {
            if (pattern.Length == 0 || rangeStart >= rangeEnd || maxMatches <= 0)
                return [];

            byte[]? buffer = vmm.MemRead(pid, rangeStart, (uint)(rangeEnd - rangeStart), out _, VmmFlags.NOCACHE);
            if (buffer is null || buffer.Length < pattern.Length)
                return [];

            var matches = new List<ulong>(Math.Min(maxMatches, 32));
            int lastStart = buffer.Length - pattern.Length;
            for (int i = 0; i <= lastStart; i++)
            {
                bool isMatch = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    var expected = pattern[j];
                    if (expected.HasValue && buffer[i + j] != expected.Value) { isMatch = false; break; }
                }
                if (!isMatch) continue;
                matches.Add(rangeStart + (ulong)i);
                if (matches.Count >= maxMatches) break;
            }
            return [.. matches];
        }

        private static bool TryParseSignature(string signature, out byte?[] pattern)
        {
            var parts = signature.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) { pattern = []; return false; }
            pattern = new byte?[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part is "?" or "??") { pattern[i] = null; continue; }
                if (part.Length != 2 || !byte.TryParse(part, System.Globalization.NumberStyles.HexNumber, null, out var b))
                { pattern = []; return false; }
                pattern[i] = b;
            }
            return true;
        }
    }
}
