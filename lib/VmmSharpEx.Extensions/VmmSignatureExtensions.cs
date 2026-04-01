using VmmSharpEx;
using VmmSharpEx.Options;

namespace VmmSharpEx.Extensions
{
    /// <summary>
    /// Extension methods for <see cref="Vmm"/> providing multi-match signature scanning.
    /// </summary>
    public static class VmmSignatureExtensions
    {
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

            byte[] buffer = vmm.MemRead(pid, rangeStart, (uint)(rangeEnd - rangeStart), out _, VmmFlags.NOCACHE);
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
