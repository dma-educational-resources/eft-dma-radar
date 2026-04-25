#pragma warning disable IDE0130

using ArenaUtils = eft_dma_radar.Arena.Misc.Utils;

namespace eft_dma_radar.Arena.Unity.IL2CPP
{
    public static partial class Il2CppDumper
    {
        // ── Schema types ─────────────────────────────────────────────────────

        private enum FieldKind { Normal, MethodRva }

        private readonly struct SchemaField(string il2cpp, string cs, FieldKind kind = FieldKind.Normal)
        {
            public readonly string Il2CppName = il2cpp;
            public readonly string CsName = cs;
            public readonly FieldKind Kind = kind;
        }

        private sealed class SchemaClass(string il2cpp, string cs, bool isStatic, SchemaField[] fields, uint? typeIndex, string? resolveViaChild = null)
        {
            public readonly string Il2CppName = il2cpp;
            public readonly string CsName = cs;
            public readonly bool IsStatic = isStatic;
            public readonly SchemaField[] Fields = fields;
            public readonly uint? TypeIndex = typeIndex;
            public readonly string? ResolveViaChild = resolveViaChild;
        }

        private static SchemaField F(string il2cpp, string? cs = null)
            => new(il2cpp, cs ?? il2cpp, FieldKind.Normal);
        private static SchemaField M(string il2cpp, string? cs = null)
            => new(il2cpp, cs ?? (il2cpp + "_RVA"), FieldKind.MethodRva);
        private static SchemaClass C(string il2cpp, SchemaField[] f, string? cs = null, bool s = false, uint ti = 0, string? child = null)
            => new(il2cpp, cs ?? il2cpp, s, f, ti == 0 ? null : ti, child);

        // ── Arena schema ─────────────────────────────────────────────────────
        // Phase 0-2: minimal schema — populates from dump output.
        // Expand after first successful run and studying the cache JSON.

        private static SchemaClass[] BuildSchema() =>
        [
            // ── Il2CppClass layout helpers ──
            // These are read-only constants and are NOT dumped.

            // ── GamePlayerOwner (singleton static class) ──────────────────────
            // Resolved via TypeIndex for O(1) lookup
            C("GamePlayerOwner", [
                F("_myPlayer"),
            ], s: true, ti: SDK.Offsets.Special.GamePlayerOwner_TypeIndex),

            // ── ClientLocalGameWorld ──────────────────────────────────────────
            // IL2CPP class name is "GameWorld"; "ClientLocalGameWorld" is the C# subclass name
            // but the type table entry is registered as "GameWorld" in IL2CPP metadata.
            C("GameWorld", [
                F("RegisteredPlayers"),
                F("MainPlayer"),
                F("<LocationId>k__BackingField", "LocationId"),
            ], cs: "ClientLocalGameWorld"),

            // ── ObservedPlayerView (Arena players) ───────────────────────────
            C("EFT.NextObservedPlayer.ObservedPlayerView", [
                F("<NickName>k__BackingField", "NickName"),
                F("<Side>k__BackingField", "Side"),
                F("<IsAI>k__BackingField", "IsAI"),
            ], cs: "ObservedPlayerView"),

            // ── ObservedPlayerController (for InventoryController ptr) ───────
            C("EFT.NextObservedPlayer.ObservedPlayerController", [
                F("<InventoryController>k__BackingField", "InventoryController"),
            ], cs: "ObservedPlayerController"),

            // ── Inventory chain (armband -> TeamID) ──────────────────────────
            // Use FQNs: multiple classes share these short names
            // (e.g. Arena.KillCamera.InventoryController).
            C("EFT.InventoryLogic.InventoryController", [
                F("<Inventory>k__BackingField", "Inventory"),
            ], cs: "InventoryController"),
            C("EFT.InventoryLogic.Inventory", [
                F("Equipment"),
            ], cs: "Inventory"),
            // Slots is declared on CompoundItem (Equipment inherits it).
            C("EFT.InventoryLogic.CompoundItem", [
                F("Slots"),
            ], cs: "CompoundItem"),
            C("EFT.InventoryLogic.Slot", [
                F("<ContainedItem>k__BackingField", "ContainedItem"),
                F("<ID>k__BackingField", "ID"),
            ], cs: "Slot"),
            C("EFT.InventoryLogic.Item", [
                F("<Template>k__BackingField", "Template"),
            ], cs: "LootItem"),
            C("EFT.InventoryLogic.ItemTemplate", [
                F("<_id>k__BackingField", "_id"),
            ], cs: "ItemTemplate"),

            // ── EFT.CameraControl.OpticCameraManager ─────────────────────────
            C("OpticCameraManager", [
                F("<Camera>k__BackingField", "Camera"),
            ]),

            // ── EFT.CameraControl.CameraManager → EFTCameraManager ───────────
            // IL2CPP class is "CameraManager"; we alias it to EFTCameraManager
            // (matches the EFT-silk naming) to avoid clashing with our own
            // CameraManager type.
            C("CameraManager", [
                F("<OpticCameraManager>k__BackingField", "OpticCameraManager"),
                F("<Camera>k__BackingField", "Camera"),
                M("get_Instance", "GetInstance_RVA"),
            ], cs: "EFTCameraManager"),
        ];

        // ── TypeIndex map ─────────────────────────────────────────────────────
        // Empty for Phase 0-2: populate after studying the dump.
        private static readonly (string Il2CppName, string FieldName)[] TypeIndexMap =
        [
            ("GamePlayerOwner", "GamePlayerOwner_TypeIndex"),
        ];

        // ── TypeIndex resolution helpers ──────────────────────────────────────

        private static readonly FieldInfo[] CachedTypeIndexFields =
            typeof(SDK.Offsets.Special).GetFields(BindingFlags.Public | BindingFlags.Static);

        private record struct SigScanResult(int Index, string Desc, string State, int Matches, int ValidMatches, ulong Rva);

        private static SigScanResult[] _lastSigResults = [];
        private static string _lastResolutionMode = "not run";

        private static void ResolveTypeIndices(
            Dictionary<string, int> nameToIndex,
            List<(string Name, string Namespace, ulong KlassPtr, int Index)> classes)
        {
            foreach (var (il2cppName, fieldName) in TypeIndexMap)
            {
                var fi = CachedTypeIndexFields.FirstOrDefault(f => f.Name == fieldName);
                if (fi is null) continue;

                int dotIdx = il2cppName.LastIndexOf('.');
                if (dotIdx > 0)
                {
                    var ns = il2cppName[..dotIdx];
                    var shortName = il2cppName[(dotIdx + 1)..];
                    bool found = false;
                    foreach (var (cName, cNs, _, cIdx) in classes)
                    {
                        if (cName == shortName && cNs == ns)
                        {
                            fi.SetValue(null, (uint)cIdx);
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                        Log.WriteLine($"[Il2CppDumper] WARN: '{il2cppName}' not found — {fieldName} using fallback.");
                }
                else if (nameToIndex.TryGetValue(il2cppName, out var index))
                    fi.SetValue(null, (uint)index);
                else
                    Log.WriteLine($"[Il2CppDumper] WARN: '{il2cppName}' not found — {fieldName} using fallback.");
            }
        }

        internal static void DebugDumpResolverState(int classCount, int updated, int fallback, int skipped)
        {
            var gaBase = Memory.GameAssemblyBase;
            string gaText = ArenaUtils.IsValidVirtualAddress(gaBase) ? $"0x{gaBase:X}" : "(not resolved)";
            Log.WriteLine($"[Il2CppDumper] ══ Arena IL2CPP Dump Summary ══");
            Log.WriteLine($"[Il2CppDumper]   GameAssembly  : {gaText}");
            Log.WriteLine($"[Il2CppDumper]   Resolution    : {_lastResolutionMode}");
            Log.WriteLine($"[Il2CppDumper]   Table RVA     : 0x{SDK.Offsets.Special.TypeInfoTableRva:X}");
            Log.WriteLine($"[Il2CppDumper]   Classes found : {classCount}");
            Log.WriteLine($"[Il2CppDumper]   Updated       : {updated}");
            Log.WriteLine($"[Il2CppDumper]   Fallback      : {fallback}");
            Log.WriteLine($"[Il2CppDumper]   Skipped       : {skipped}");
            foreach (var r in _lastSigResults)
                Log.WriteLine($"[Il2CppDumper]   Sig[{r.Index}] {r.State,-7} matches={r.Matches,-4} valid={r.ValidMatches,-4} rva=0x{r.Rva:X}  — {r.Desc}");
        }
    }
}
