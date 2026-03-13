using System.IO;
using System.Text;
using eft_dma_radar.Common.Misc;
using SDK;

namespace eft_dma_radar.Tarkov.Unity.IL2CPP
{
    public static class Il2CppDumper
    {
        // ── IL2CPP struct field offsets ──────────────────────────────────────────
        private const uint K_Name        = 0x10;   // char*  Il2CppClass::name
        private const uint K_Namespace   = 0x18;   // char*  Il2CppClass::namespaze
        private const uint K_Fields      = 0x80;   // FieldInfo*   (direct array)
        private const uint K_Methods     = 0x98;   // MethodInfo** (array of pointers)
        private const uint K_MethodCount = 0x120;  // uint16
        private const uint K_FieldCount  = 0x124;  // uint16

        private const uint FI_Name       = 0x00;   // char*  FieldInfo::name
        private const uint FI_Offset     = 0x18;   // int32  FieldInfo::offset
        private const uint FI_Stride     = 0x20;   // sizeof(FieldInfo)

        private const uint MI_Pointer    = 0x00;   // void*  MethodInfo::methodPointer
        private const uint MI_Name       = 0x18;   // char*  MethodInfo::name

        private const int  MaxClasses    = 80_000;
        private const int  MaxNameLen    = 256;

        // ── Schema ───────────────────────────────────────────────────────────────

        private enum FieldKind { Normal, MethodRva }

        private readonly struct SchemaField
        {
            public readonly string    Il2CppName; // name as it appears in IL2CPP metadata
            public readonly string    CsName;     // name to emit in the output struct
            public readonly FieldKind Kind;
            public SchemaField(string il2cpp, string cs, FieldKind kind = FieldKind.Normal)
            { Il2CppName = il2cpp; CsName = cs; Kind = kind; }
        }

        private sealed class SchemaClass
        {
            public readonly string         Il2CppName;  // actual IL2CPP class name for lookup
            public readonly string         CsName;      // struct/class name in generated output
            public readonly bool           IsStatic;    // emit as static class (singleton statics)
            public readonly SchemaField[]  Fields;

            public SchemaClass(string il2cpp, string cs, bool isStatic, SchemaField[] fields)
            { Il2CppName = il2cpp; CsName = cs; IsStatic = isStatic; Fields = fields; }
        }

        // Shorthand helpers
        private static SchemaField F(string il2cpp, string cs = null)
            => new(il2cpp, cs ?? il2cpp, FieldKind.Normal);
        private static SchemaField M(string il2cpp, string cs = null)
            => new(il2cpp, cs ?? (il2cpp + "_RVA"), FieldKind.MethodRva);
        private static SchemaClass C(string il2cpp, SchemaField[] f, string cs = null, bool s = false)
            => new(il2cpp, cs ?? il2cpp, s, f);

        private static SchemaClass[] BuildSchema() =>
        [
            C("AFKMonitor",                       [F("_lastInput")],                                                                                                          cs: "AfkMonitor"),
            C("ClientLocalGameWorld",             [F("RegisteredPlayers"), F("LootList"), F("ExfilController"), F("Grenades")]),
            C("ArtilleryShellingControllerClient",[F("_shellExplosionPositions")],                                                                                            cs: "ClientShellingController"),
            C("WorldInteractiveObject",           [F("DoorState"), F("Id"), F("KeyId")],                                                                                      cs: "Interactable"),
            C("ExfiltrationController",           [F("ExfiltrationPoints"), F("ScavExfiltrationPoints")],                                                                     cs: "ExfilController"),
            C("ExfiltrationPoint",                [F("Status"), F("Settings"), F("EligibleIds")],                                                                             cs: "Exfil"),
            C("ScavExfiltrationPoint",            [F("Status"), F("Settings"), F("EligibleIds"), F("EligibleProfiles")],                                                      cs: "ScavExfil"),
            C("ExitTriggerSettings",              [F("Name"), F("PlayersCount"), F("ExfiltrationType"), F("RequiredSlot")],                                                    cs: "ExfilSettings"),
            C("Throwable",                        [F("IsDestroyed")],                                                                                                         cs: "Grenade",  s: true),
            C("ObservedPlayerStateContext",       [F("Rotation"), F("Velocity"), F("PoseLevel"), F("Tilt"), F("Prone"), F("Sprinting"), F("HasGround")],                       cs: "ObservedMovementController"),
            C("ObservedPlayerHandsController",    [F("Item"), F("FireMode"), F("IsAiming"), F("IsThrowingPatched")],                                                           cs: "ObservedHandsController"),
            C("BundleAnimationBones",             [F("_rightHand"), F("_leftHand")],                                                                                          cs: "BundleAnimationBonesController"),
            C("ObservedPlayerHealthController",   [F("_bodyPartDic")],                                                                                                        cs: "ObservedHealthController"),
            C("IHealthController",                [F("_bodyPartEffectHandlers")],                                                                                              cs: "HealthController"),
            C("PlayerSpring",                     [F("_camera")],                                                                                                              cs: "HandsContainer"),
            C("NewRecoilShotEffect",              [F("_currentIntensity"), F("_targetIntensity"), F("_damping"), F("_speed")],                                                 cs: "NewShotRecoil"),
            C("ProfileInfo",                      [F("Nickname"), F("Side"), F("Experience"), F("RegistrationDate"), F("MemberCategory")],                                    cs: "PlayerInfo"),
            C("ProfileSettings",                  [F("Role"), F("BotDifficulty")],                                                                                            cs: "PlayerInfoSettings"),
            C("FloatBuff",                        [F("_value")],                                                                                                               cs: "SkillValueContainer"),
            C("QuestStatusData",                  [F("Id"), F("Status"), F("CompletedConditions")],                                                                            cs: "QuestData"),
            C("ConditionCollection",              [F("_conditions")],                                                                                                          cs: "QuestConditionsContainer"),
            C("ItemController",                   [F("_rootItem")],                                                                                                            cs: "LootableContainerItemOwner"),
            C("CompoundItem",                     [F("Grids"), F("Slots")],                                                                                                    cs: "Equipment"),
            C("Item",                             [F("Id"), F("Template")],                                                                                                    cs: "LootItem"),
            C("Weapon",                           [F("Chambers"), F("MagazineSlot")],                                                                                         cs: "LootItemWeapon"),
            C("Magazine",                         [F("Count"), F("MaxCount")],                                                                                                cs: "LootItemMagazine"),
            C("SlotView_2",                       [F("_slot")],                                                                                                                cs: "PlayerBodySubclass"),
            C("PhysicalBase",                     [F("Stamina"), F("HandsStamina"), F("Oxygen")],                                                                              cs: "Physical"),
            C("Stamina",                          [F("Current"), F("Capacity")],                                                                                              cs: "PhysicalValue"),
            C("CameraManager",                    [F("_instance"), M("get_Instance", "get_Instance_RVA")],                                                                    cs: "EFTCameraManager"),
            C("SightModTemplate",                 [F("ScopesCount"), F("Zooms"), F("AimingZ"), F("AngularShotpen")],                                                           cs: "SightInterface"),
            C("Player",                           [F("_playerBody"), F("Profile"), F("MovementContext"), F("HandsController"), F("HealthController"), F("Physical"), F("IsLocalPlayer")]),
            C("Profile",                          [F("Info"), F("Skills"), F("Id")]),
            C("SkillManager",                     [F("Endurance"), F("Strength"), F("Vitality"), F("Health"), F("StressResistance"), F("Metabolism"), F("Perception"), F("Attention"), F("Covert")]),
        ];

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Dump field offsets and method RVAs for all schema-defined classes from live memory.
        /// Writes a file at <paramref name="outputPath"/> that is compatible with the SDK.cs format:
        ///   namespace SDK { public readonly partial struct Offsets { ... } }
        /// Call this once during game startup after <c>Memory.GameAssemblyBase</c> is valid.
        /// </summary>
        public static void Dump(string outputPath)
        {
            XMLogging.WriteLine("[Il2CppDumper] Dump starting...");

            var gaBase = Memory.GameAssemblyBase;
            if (gaBase == 0)
            {
                XMLogging.WriteLine("[Il2CppDumper] ERROR: GameAssemblyBase is 0 — game not ready.");
                return;
            }

            var schema  = BuildSchema();
            var classes = ReadAllClasses();

            // IL2CPP class name → klassPtr (first match wins)
            var lookup = new Dictionary<string, ulong>(StringComparer.Ordinal);
            foreach (var (name, _, ptr, _) in classes)
                lookup.TryAdd(name, ptr);

            XMLogging.WriteLine($"[Il2CppDumper] Type table: {classes.Count} classes found.");

            var sb = new StringBuilder(128 * 1024);
            sb.AppendLine("// Auto-generated by Il2CppDumper");
            sb.AppendLine($"// {DateTime.UtcNow:u}");
            sb.AppendLine();
            sb.AppendLine("namespace SDK");
            sb.AppendLine("{");
            sb.AppendLine("    public readonly partial struct Offsets");
            sb.AppendLine("    {");

            int dumped = 0, skipped = 0;

            foreach (var sc in schema)
            {
                if (!lookup.TryGetValue(sc.Il2CppName, out var klassPtr))
                {
                    XMLogging.WriteLine($"[Il2CppDumper] SKIP '{sc.Il2CppName}': not found in type table.");
                    skipped++;
                    continue;
                }

                var fieldMap  = ReadClassFields(klassPtr);
                var methodMap = ReadClassMethods(klassPtr, gaBase);

                var keyword = sc.IsStatic ? "static class" : "readonly partial struct";
                sb.AppendLine($"        public {keyword} {sc.CsName}");
                sb.AppendLine("        {");

                foreach (var sf in sc.Fields)
                {
                    if (sf.Kind == FieldKind.MethodRva)
                    {
                        // Look up by the IL2CPP method name (strip _RVA suffix if present)
                        var methodName = sf.Il2CppName.EndsWith("_RVA", StringComparison.Ordinal)
                            ? sf.Il2CppName[..^4]
                            : sf.Il2CppName;

                        if (methodMap.TryGetValue(methodName, out var rva))
                            sb.AppendLine($"            public const ulong {sf.CsName} = 0x{rva:X};");
                        else
                            XMLogging.WriteLine($"[Il2CppDumper] WARN: method '{methodName}' not found in '{sc.Il2CppName}'.");
                    }
                    else
                    {
                        if (fieldMap.TryGetValue(sf.Il2CppName, out var offset))
                            sb.AppendLine($"            public const uint {sf.CsName} = 0x{offset:X};");
                        else
                            XMLogging.WriteLine($"[Il2CppDumper] WARN: field '{sf.Il2CppName}' not found in '{sc.Il2CppName}'.");
                    }
                }

                sb.AppendLine("        }");
                sb.AppendLine();
                dumped++;
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
            XMLogging.WriteLine($"[Il2CppDumper] Done. {dumped} classes dumped, {skipped} skipped → {outputPath}");
        }

        // ── Memory helpers ───────────────────────────────────────────────────────

        private static List<(string Name, string Namespace, ulong KlassPtr, int Index)> ReadAllClasses()
        {
            var result = new List<(string, string, ulong, int)>(4096);
            var gaBase = Memory.GameAssemblyBase;
            if (gaBase == 0) return result;

            // TypeInfoTableRva points to a pointer that holds the actual Il2CppClass*[] table.
            // Must dereference once (ReadPtr) to get the table base, then ReadArray entries
            // are direct Il2CppClass* — no second dereference.
            ulong tablePtr;
            try { tablePtr = Memory.ReadPtr(gaBase + Offsets.Special.TypeInfoTableRva, false); }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Il2CppDumper] ReadPtr(TypeInfoTableRva) failed: {ex.Message}");
                return result;
            }

            if (!tablePtr.IsValidVirtualAddress())
            {
                XMLogging.WriteLine("[Il2CppDumper] TypeInfoTable pointer is invalid.");
                return result;
            }

            ulong[] ptrs;
            try { ptrs = Memory.ReadArray<ulong>(tablePtr, MaxClasses, false); }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Il2CppDumper] ReadArray failed: {ex.Message}");
                return result;
            }

            for (int i = 0; i < ptrs.Length; i++)
            {
                // Each entry is a direct Il2CppClass* — validate and use directly
                var klassPtr = ptrs[i];
                if (!klassPtr.IsValidVirtualAddress()) continue;

                var name = ReadStr(ReadPtr(klassPtr + K_Name));
                if (string.IsNullOrEmpty(name)) continue;

                var ns = ReadStr(ReadPtr(klassPtr + K_Namespace)) ?? string.Empty;
                result.Add((name, ns, klassPtr, i));
            }

            return result;
        }

        private static Dictionary<string, int> ReadClassFields(ulong klassPtr)
        {
            var result     = new Dictionary<string, int>(StringComparer.Ordinal);
            var fieldCount = Memory.ReadValue<ushort>(klassPtr + K_FieldCount, false);
            if (fieldCount == 0 || fieldCount > 4096) return result;

            var fieldsBase = ReadPtr(klassPtr + K_Fields);
            if (!fieldsBase.IsValidVirtualAddress()) return result;

            for (int i = 0; i < fieldCount; i++)
            {
                var entry  = fieldsBase + (ulong)(i * FI_Stride);
                var namePtr = ReadPtr(entry + FI_Name);
                if (!namePtr.IsValidVirtualAddress()) continue;

                var name = ReadStr(namePtr);
                if (string.IsNullOrEmpty(name)) continue;

                var offset = Memory.ReadValue<int>(entry + FI_Offset, false);
                result.TryAdd(name, offset);
            }

            return result;
        }

        private static Dictionary<string, ulong> ReadClassMethods(ulong klassPtr, ulong gaBase)
        {
            var result      = new Dictionary<string, ulong>(StringComparer.Ordinal);
            var methodCount = Memory.ReadValue<ushort>(klassPtr + K_MethodCount, false);
            if (methodCount == 0 || methodCount > 4096) return result;

            var methodsBase = ReadPtr(klassPtr + K_Methods);
            if (!methodsBase.IsValidVirtualAddress()) return result;

            ulong[] methodPtrs;
            try { methodPtrs = Memory.ReadArray<ulong>(methodsBase, methodCount, false); }
            catch { return result; }

            foreach (var mp in methodPtrs)
            {
                if (!mp.IsValidVirtualAddress()) continue;

                var fnPtr = Memory.ReadValue<ulong>(mp + MI_Pointer, false);
                if (!fnPtr.IsValidVirtualAddress() || fnPtr < gaBase) continue;

                var namePtr = ReadPtr(mp + MI_Name);
                if (!namePtr.IsValidVirtualAddress()) continue;

                var name = ReadStr(namePtr);
                if (string.IsNullOrEmpty(name)) continue;

                var rva = fnPtr - gaBase;
                result.TryAdd(name, rva);
            }

            return result;
        }

        // Reads a pointer without throwing (uses ReadValue<ulong> — never ReadPtr which throws)
        private static ulong ReadPtr(ulong addr)
        {
            if (!addr.IsValidVirtualAddress()) return 0;
            try { return Memory.ReadValue<ulong>(addr, false); }
            catch { return 0; }
        }

        private static string ReadStr(ulong addr)
        {
            if (!addr.IsValidVirtualAddress()) return null;
            try { return Memory.ReadString(addr, MaxNameLen, false); }
            catch { return null; }
        }
    }
}
