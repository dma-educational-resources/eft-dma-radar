using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace eft_dma_radar.Common.Misc.Data
{
    public static class OffsetManager
    {
        private static readonly Dictionary<string, uint> uintOffsets = new();
        private static readonly Dictionary<string, string> stringOffsets = new();

        public static void LoadOffsets(string sdkPath)
        {
            if (!File.Exists(sdkPath))
                throw new FileNotFoundException($"SDK.cs not found at {sdkPath}");

            XMLogging.WriteLine($"[OffsetManager] Found SDK.cs at: {sdkPath}");

            var lines = File.ReadAllLines(sdkPath);
            var uintPattern = new Regex(@"public const uint (?<name>\w+) = (?<value>0x[0-9A-Fa-f]+);");
            var stringPattern = new Regex(@"public const string (?<name>\w+) = @\""(?<value>.*?)\"";");

            string currentStruct = string.Empty;
            int uintCount = 0;
            int stringCount = 0;

            foreach (var line in lines)
            {
                var structMatch = Regex.Match(line, @"(?:readonly\s+)?partial\s+struct\s+(\w+)|readonly\s+struct\s+(\w+)");
                if (structMatch.Success)
                {
                    currentStruct = structMatch.Groups[1].Success ? structMatch.Groups[1].Value : structMatch.Groups[2].Value;
                    XMLogging.WriteLine($"[OffsetManager] Parsing struct: {currentStruct}");
                }
                else if (uintPattern.IsMatch(line))
                {
                    try
                    {
                        var match = uintPattern.Match(line);
                        var key = $"{currentStruct}.{match.Groups["name"].Value}";
                        var value = Convert.ToUInt32(match.Groups["value"].Value, 16);
                        uintOffsets[key] = value;
                        uintCount++;
                    }
                    catch (Exception ex)
                    {
                        XMLogging.WriteLine($"[OffsetManager] Failed parsing uint line: {line}\nException: {ex}");
                    }
                }
                else if (stringPattern.IsMatch(line))
                {
                    try
                    {
                        var match = stringPattern.Match(line);
                        var key = $"{currentStruct}.{match.Groups["name"].Value}";
                        stringOffsets[key] = match.Groups["value"].Value;
                        stringCount++;
                    }
                    catch (Exception ex)
                    {
                        XMLogging.WriteLine($"[OffsetManager] Failed parsing string line: {line}\nException: {ex}");
                    }
                }
            }

            XMLogging.WriteLine($"[OffsetManager] Loaded {uintCount} uint offsets, {stringCount} string offsets.");

            if (uintCount == 0 && stringCount == 0)
                XMLogging.WriteLine("[OffsetManager] ⚠️ Warning: No offsets found in SDK.cs!");

            XMLogging.WriteLine("[OffsetManager] OffsetManager fully initialized.");
        }

        public static uint GetOffset(string name)
        {
            if (!uintOffsets.TryGetValue(name, out var value))
                throw new KeyNotFoundException($"Offset '{name}' not found.");

            return value;
        }

        public static string GetClassName(string name)
        {
            if (!stringOffsets.TryGetValue(name, out var value))
                throw new KeyNotFoundException($"ClassName '{name}' not found.");

            return value;
        }
    }
}