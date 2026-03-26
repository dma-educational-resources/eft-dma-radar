using SDK;

namespace eft_dma_radar.Tarkov.Unity.IL2CPP
{
    public static partial class Il2CppDumper
    {
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
            public readonly string        Il2CppName; // plain class name used for name-based lookup
            public readonly string        CsName;     // struct name in generated output
            public readonly bool          IsStatic;   // emit as static class (singleton statics)
            public readonly SchemaField[] Fields;
            /// <summary>
            /// When non-null, resolves the class directly via
            ///   tablePtr + TypeIndex * 8
            /// without name-string matching. Required for obfuscated EFT classes.
            /// Obtain from Offsets.Special or by scanning the type table offline.
            /// MDToken → TypeIndex: (mdToken &amp; 0x00FFFFFF) - 1
            /// </summary>
            public readonly uint? TypeIndex;

            public SchemaClass(string il2cpp, string cs, bool isStatic, SchemaField[] fields, uint? typeIndex)
            { Il2CppName = il2cpp; CsName = cs; IsStatic = isStatic; Fields = fields; TypeIndex = typeIndex; }
        }

        // Shorthand helpers
        private static SchemaField F(string il2cpp, string cs = null)
            => new(il2cpp, cs ?? il2cpp, FieldKind.Normal);
        private static SchemaField M(string il2cpp, string cs = null)
            => new(il2cpp, cs ?? (il2cpp + "_RVA"), FieldKind.MethodRva);

        /// <param name="il2cpp">Plain IL2CPP class name (only used for name-based fallback).</param>
        /// <param name="f">Fields / methods to dump.</param>
        /// <param name="cs">Output struct name (defaults to il2cpp).</param>
        /// <param name="s">Emit as static class.</param>
        /// <param name="ti">
        /// TypeIndex for direct O(1) lookup.
        /// Set this for any class whose name is obfuscated in EFT (\uXXXX).
        /// Obtain via: (MDToken &amp; 0x00FFFFFF) - 1, or from Offsets.Special.
        /// Leave 0 to use name-based lookup (only reliable for non-obfuscated classes).
        /// </param>
        private static SchemaClass C(string il2cpp, SchemaField[] f, string cs = null, bool s = false, uint ti = 0)
            => new(il2cpp, cs ?? il2cpp, s, f, ti == 0 ? null : ti);

        private static SchemaClass[] BuildSchema() =>
        [
            // TarkovApplication
            C("TarkovApplication", [F("_menuOperation")]),

            // MainMenuShowOperation
            C("MainMenuShowOperation", [F("_afkMonitor"), F("_preloaderUI"), F("_profile")]),

            // PreloaderUI
            C("PreloaderUI", [F("_sessionIdText"), F("_alphaVersionLabel")]),

            // AFKMonitor → AfkMonitor
            C("AFKMonitor", [F("_afkTimeout", "Delay")], cs: "AfkMonitor"),

            // GameWorld (base fields)
            C("GameWorld", [
                F("GameDateTime"),
                F("<SynchronizableObjectLogicProcessor>k__BackingField", "SynchronizableObjectLogicProcessor"),
            ]),

            // GameWorld → ClientLocalGameWorld (extended fields from same IL2CPP class)
            C("GameWorld", [
                F("<BtrController>k__BackingField", "BtrController"),
                F("<TransitController>k__BackingField", "TransitController"),
                F("<ExfiltrationController>k__BackingField", "ExfilController"),
                F("<ClientShellingController>k__BackingField", "ClientShellingController"),
                F("<LocationId>k__BackingField", "LocationId"),
                F("LootList"),
                F("RegisteredPlayers"),
                F("BorderZones"),
                F("MainPlayer"),
                F("_world", "World"),
                F("<SynchronizableObjectLogicProcessor>k__BackingField", "SynchronizableObjectLogicProcessor"),
                F("Grenades"),
            ], cs: "ClientLocalGameWorld"),

            // TransitController
            C("TransitController", [F("pointsById", "TransitPoints")]),

            // ArtilleryShellingControllerClient → ClientShellingController
            C("ArtilleryShellingControllerClient", [F("ActiveClientProjectiles")], cs: "ClientShellingController"),

            // World_2 → WorldController
            C("World_2", [F("_interactables", "Interactables")], cs: "WorldController"),

            // WorldInteractiveObject → Interactable
            C("WorldInteractiveObject", [F("KeyId"), F("Id"), F("_doorState")], cs: "Interactable"),

            // ArtilleryProjectileClient
            C("ArtilleryProjectileClient", [F("_targetPosition", "Position"), F("_flyOn", "IsActive")]),

            // TransitPoint
            C("TransitPoint", [F("parameters")]),

            // TransitParameters
            C("TransitParameters", [F("id"), F("active"), F("name"), F("description"), F("target"), F("location")]),

            // SynchronizableObject
            C("SynchronizableObject", [F("Type")]),

            // SynchronizableObjectLogicProcessor
            C("SynchronizableObjectLogicProcessor", [F("_activeSynchronizableObjects")]),

            // TripwireSynchronizableObject
            C("TripwireSynchronizableObject", [
                F("<GrenadeTemplateId>k__BackingField", "GrenadeTemplateId"),
                F("_tripwireState"),
                F("<FromPosition>k__BackingField", "FromPosition"),
                F("<ToPosition>k__BackingField", "ToPosition"),
            ]),

            // BtrController
            C("BtrController", [F("<BtrView>k__BackingField", "BtrView")]),

            // BTRView
            C("BTRView", [F("turret"), F("_previousPosition")]),

            // BTRTurretView
            C("BTRTurretView", [F("_bot", "AttachedBot")]),

            // EffectsController
            C("EffectsController", [
                F("_effectsPrefab"),
                F("FastVineteFlicker"),
                F("<RainScreenDrops>k__BackingField", "RainScreenDrops"),
                F("<ScreenWater>k__BackingField", "ScreenWater"),
                F("_vignette"),
                F("_doubleVision"),
                F("_hueFocus"),
                F("_radialBlur"),
                F("_sharpen"),
                F("_lowhHealthBlend"),
                F("_bloodlossBlend"),
                F("_wiggle"),
                F("_motionBluer"),
                F("_bloodOnScreen"),
                F("_grenadeFlash"),
                F("_eyeBurn"),
                F("_blur"),
                F("_dof"),
                F("_effectAccumulators"),
                F("_sharpenAccumulator"),
                F("_radialBlurAccumulator"),
                F("_chromaticAberration"),
                F("_thermalVision"),
                F("_frostbiteEffect"),
            ]),

            // FrostbiteEffect
            C("FrostbiteEffect", [F("_opacity")]),

            // NightVision
            C("NightVision", [F("_on")]),

            // ThermalVision
            C("ThermalVision", [
                F("_material", "Material"), F("On"), F("IsNoisy"), F("IsFpsStuck"), F("IsMotionBlurred"),
                F("IsGlitch"), F("IsPixelated"), F("ChromaticAberrationThermalShift"),
                F("UnsharpRadiusBlur"), F("UnsharpBias"),
            ]),

            // HealthInfo → HealthController
            C("HealthInfo", [F("Energy"), F("Hydration")], cs: "HealthController"),

            // ExfiltrationController → ExfilController
            C("ExfiltrationController", [
                F("<ExfiltrationPoints>k__BackingField", "ExfiltrationPointArray"),
                F("<ScavExfiltrationPoints>k__BackingField", "ScavExfiltrationPointArray"),
                F("<SecretExfiltrationPoints>k__BackingField", "SecretExfiltrationPointArray"),
            ], cs: "ExfilController"),

            // ExfiltrationPoint → Exfil
            C("ExfiltrationPoint", [F("_status"), F("Settings"), F("EligibleEntryPoints")], cs: "Exfil"),

            // ScavExfiltrationPoint → ScavExfil
            C("ScavExfiltrationPoint", [F("EligibleIds")], cs: "ScavExfil"),

            // ExitTriggerSettings → ExfilSettings
            C("ExitTriggerSettings", [F("Name")], cs: "ExfilSettings"),

            // Grenade (fields from Grenade class)
            C("Grenade", [F("<WeaponSource>k__BackingField", "WeaponSource")], cs: "Grenade"),

            // Throwable (fields from Throwable class → same output struct Grenade)
            C("Throwable", [F("_isDestroyed", "IsDestroyed")], cs: "Grenade"),

            // Player
            C("Player", [
                F("_characterController"),
                F("<MovementContext>k__BackingField", "MovementContext"),
                F("_playerBody"),
                F("<ProceduralWeaponAnimation>k__BackingField", "ProceduralWeaponAnimation"),
                F("_animators"),
                F("EnabledAnimators"),
                F("Corpse"),
                F("<Location>k__BackingField", "Location"),
                F("<InteractableObject>k__BackingField", "InteractableObject"),
                F("<Profile>k__BackingField", "Profile"),
                F("Physical"),
                F("<AIData>k__BackingField", "AIData"),
                F("_healthController"),
                F("_inventoryController"),
                F("_handsController"),
                F("<InteractionRayOriginOnStartOperation>k__BackingField", "InteractionRayOriginOnStartOperation"),
                F("<InteractionRayDirectionOnStartOperation>k__BackingField", "InteractionRayDirectionOnStartOperation"),
                F("<IsYourPlayer>k__BackingField", "IsYourPlayer"),
                F("<VoipID>k__BackingField", "VoipID"),
                F("<PlayerId>k__BackingField", "Id"),
                F("<GameWorld>k__BackingField", "GameWorld"),
            ]),

            // ObservedPlayerView
            C("ObservedPlayerView", [
                F("<ObservedPlayerController>k__BackingField", "ObservedPlayerController"),
                F("<Voice>k__BackingField", "Voice"),
                F("<VisibleToCameraType>k__BackingField", "VisibleToCameraType"),
                F("<GroupId>k__BackingField", "GroupID"),
                F("<Side>k__BackingField", "Side"),
                F("<IsAI>k__BackingField", "IsAI"),
                F("<NickName>k__BackingField", "NickName"),
                F("<AccountId>k__BackingField", "AccountId"),
                F("<PlayerBody>k__BackingField", "PlayerBody"),
                F("<Id>k__BackingField", "Id"),
                F("<VoipID>k__BackingField", "VoipId"),
            ]),

            // ObservedPlayerController
            C("ObservedPlayerController", [
                F("<InventoryController>k__BackingField", "InventoryController"),
                F("<PlayerView>k__BackingField", "Player"),
                F("<InfoContainer>k__BackingField", "InfoContainer"),
                F("<MovementController>k__BackingField", "MovementController"),
                F("<HealthController>k__BackingField", "HealthController"),
                F("<HandsController>k__BackingField", "HandsController"),
            ]),

            // ObservedPlayerStateContext → ObservedMovementController
            C("ObservedPlayerStateContext", [
                F("<Rotation>k__BackingField", "Rotation"),
                F("_velocity", "Velocity"),
            ], cs: "ObservedMovementController"),

            // ObservedPlayerHandsController → ObservedHandsController
            C("ObservedPlayerHandsController", [
                F("_item", "ItemInHands"),
                F("_bundleAnimationBones", "BundleAnimationBones"),
            ], cs: "ObservedHandsController"),

            // BundleAnimationBones → BundleAnimationBonesController
            C("BundleAnimationBones", [
                F("<ProceduralWeaponAnimation>k__BackingField", "ProceduralWeaponAnimationObs"),
            ], cs: "BundleAnimationBonesController"),

            // ProceduralWeaponAnimation → ProceduralWeaponAnimationObs (observed _isAiming)
            C("ProceduralWeaponAnimation", [
                F("_isAiming", "_isAimingObs"),
            ], cs: "ProceduralWeaponAnimationObs"),

            // ObservedPlayerHealthController → ObservedHealthController
            C("ObservedPlayerHealthController", [
                F("_player", "Player"),
                F("_playerCorpse", "PlayerCorpse"),
                F("HealthStatus"),
            ], cs: "ObservedHealthController"),

            // ProceduralWeaponAnimation (main)
            C("ProceduralWeaponAnimation", [
                F("<ShotNeedsFovAdjustments>k__BackingField", "ShotNeedsFovAdjustments"),
                F("Breath"),
                F("PositionZeroSum"),
                F("Shootingg"),
                F("_aimingSpeed"),
                F("_isAiming"),
                F("_optics"),
                F("_shotDirection"),
                F("Mask"),
                F("HandsContainer"),
                F("_fovCompensatoryDistance"),
            ]),

            // PlayerSpring → HandsContainer
            C("PlayerSpring", [
                F("CameraOffset"),
                F("HandsRotation"),
                F("CameraRotation"),
                F("CameraPosition"),
            ], cs: "HandsContainer"),

            // SightNBone
            C("SightNBone", [F("Mod")]),

            // ShotEffector
            C("ShotEffector", [F("NewShotRecoil")]),

            // PlayerStateContainer
            C("PlayerStateContainer", [F("Name"), F("StateFullNameHash")]),

            // NewRecoilShotEffect → NewShotRecoil
            C("NewRecoilShotEffect", [F("IntensitySeparateFactors")], cs: "NewShotRecoil"),

            // VisorEffect
            C("VisorEffect", [F("Intensity")]),

            // TOD_Time
            C("TOD_Time", [F("LockCurrentTime")]),

            // TOD_CycleParameters
            C("TOD_CycleParameters", [F("Hour")]),

            // TOD_ImageEffect → TOD_Scattering
            C("TOD_ImageEffect", [F("_sky", "Sky")], cs: "TOD_Scattering"),

            // TOD_Sky
            C("TOD_Sky", [
                F("<Cycle>k__BackingField", "Cycle"),
                F("<Components>k__BackingField", "TOD_Components"),
            ]),

            // TOD_Components
            C("TOD_Components", [F("<Time>k__BackingField", "TOD_Time")]),

            // Profile
            C("Profile", [
                F("Id"), F("AccountId"), F("Info"), F("Inventory"), F("Skills"),
                F("TaskConditionCounters"), F("QuestsData"), F("WishlistManager"), F("Stats"),
            ]),

            // WishlistManager
            C("WishlistManager", [F("_userItems", "Items")]),

            // ProfileInfo → PlayerInfo
            C("ProfileInfo", [
                F("Nickname"), F("EntryPoint"), F("<Side>k__BackingField", "Side"), F("RegistrationDate"),
                F("GroupId"), F("<Settings>k__BackingField", "Settings"), F("MemberCategory"), F("_experience", "Experience"),
            ], cs: "PlayerInfo"),

            // SkillManager
            C("SkillManager", [
                F("StrengthBuffJumpHeightInc"), F("StrengthBuffThrowDistanceInc"),
                F("MagDrillsLoadSpeed"), F("MagDrillsUnloadSpeed"),
                F("RaidLoadedAmmoAction"), F("RaidUnloadedAmmoAction"),
            ]),

            // FloatBuff → SkillValueContainer
            C("FloatBuff", [F("Value")], cs: "SkillValueContainer"),

            // QuestStatusData → QuestData
            C("QuestStatusData", [F("Id"), F("Status"), F("CompletedConditions"), F("Template")], cs: "QuestData"),

            // CompletedConditionsCollection
            C("CompletedConditionsCollection", [
                F("_backendData", "BackendData"),
                F("_localChanges", "LocalChanges"),
            ]),

            // QuestTemplate
            C("QuestTemplate", [
                F("<Conditions>k__BackingField", "Conditions"),
                F("_questName", "Name"),
            ]),

            // ItemHandsController
            C("ItemHandsController", [F("_item", "Item")]),

            // FirearmController
            C("FirearmController", [F("Fireport"), F("COI", "TotalCenterOfImpact"), F("WeaponLn")]),

            // ClientFirearmController (fields from ClientFirearmController + inherited FirearmController)
            C("FirearmController", [F("WeaponLn")], cs: "ClientFirearmController"),
            C("ClientFirearmController", [F("LastShotId", "ShotIndex")], cs: "ClientFirearmController"),

            // MovementContext
            C("MovementContext", [
                F("_player", "Player"),
                F("_rotation"),
                F("PlantState"),
                F("<CurrentState>k__BackingField", "CurrentState"),
                F("_states"),
                F("_movementStates"),
                F("_tilt"),
                F("_physicalCondition"),
                F("_speedLimitIsDirty"),
                F("<StateSpeedLimit>k__BackingField", "StateSpeedLimit"),
                F("<StateSprintSpeedLimit>k__BackingField", "StateSprintSpeedLimit"),
                F("_lookDirection"),
                F("<WalkInertia>k__BackingField", "WalkInertia"),
                F("<SprintBrakeInertia>k__BackingField", "SprintBrakeInertia"),
                F("_poseInertia"),
                F("_currentPoseInertia"),
                F("_inertiaAppliedTime"),
            ]),

            // MovementState (from MovementState class)
            C("MovementState", [F("StickToGround"), F("PlantTime")], cs: "MovementState"),

            // BaseMovementState (from BaseMovementState class → same output)
            C("BaseMovementState", [F("Name"), F("AnimatorStateHash"), F("AuthoritySpeed")], cs: "MovementState"),

            // MovePlayerState (from MovePlayerState class → same output)
            C("MovePlayerState", [F("_velocity"), F("_velocity2")], cs: "MovementState"),

            // InventoryController
            C("InventoryController", [F("<Inventory>k__BackingField", "Inventory")]),

            // Inventory
            C("Inventory", [F("Equipment"), F("QuestRaidItems"), F("QuestStashItems"), F("Stash")]),

            // Stash
            C("Stash", [F("_grid", "Grids")]),

            // CompoundItem → Stash (Slots from CompoundItem, same output Stash)
            C("CompoundItem", [F("Slots")], cs: "Stash"),

            // CompoundItem → Equipment
            C("CompoundItem", [F("Grids"), F("Slots")], cs: "Equipment"),

            // BarterOther → BarterOtherOffsets
            C("BarterOther", [F("Dogtag")], cs: "BarterOtherOffsets"),

            // DogtagComponent
            C("DogtagComponent", [
                F("GroupId"), F("AccountId"), F("ProfileId"), F("Nickname"),
                F("Side"), F("Level"), F("Time"), F("Status"), F("KillerAccountId"),
                F("KillerProfileId"), F("KillerName"), F("WeaponName"), F("CarriedByGroupMember"),
            ]),

            // Grid → Grids
            C("Grid", [F("<ItemCollection>k__BackingField", "ContainedItems")], cs: "Grids"),

            // GridItemCollection → GridContainedItems
            C("GridItemCollection", [F("ItemsList", "Items")], cs: "GridContainedItems"),

            // Slot
            C("Slot", [
                F("<ContainedItem>k__BackingField", "ContainedItem"),
                F("<ID>k__BackingField", "ID"),
                F("Required"),
            ]),

            // LootItem → InteractiveLootItem
            C("LootItem", [F("_item", "Item")], cs: "InteractiveLootItem"),

            // Skeleton → DizSkinningSkeleton
            C("Skeleton", [F("_values")], cs: "DizSkinningSkeleton"),

            // LootableContainer (fields from LootableContainer class)
            C("LootableContainer", [F("ItemOwner"), F("Template")], cs: "LootableContainer"),

            // WorldInteractiveObject (fields inherited → same output LootableContainer)
            C("WorldInteractiveObject", [
                F("<InteractingPlayer>k__BackingField", "InteractingPlayer"),
            ], cs: "LootableContainer"),

            // ItemController → LootableContainerItemOwner
            C("ItemController", [F("<RootItem>k__BackingField", "RootItem")], cs: "LootableContainerItemOwner"),

            // Item → LootItem
            C("Item", [
                F("StackObjectsCount"), F("Version"), F("Components"), F("<Template>k__BackingField", "Template"), F("<SpawnedInSession>k__BackingField", "SpawnedInSession"),
            ], cs: "LootItem"),

            // CompoundItem → LootItemMod
            C("CompoundItem", [F("Grids"), F("Slots")], cs: "LootItemMod"),

            // Grid → Grid
            C("Grid", [F("<ItemCollection>k__BackingField", "ItemCollection")], cs: "Grid"),

            // GridItemCollection → GridItemCollection
            C("GridItemCollection", [F("ItemsList")], cs: "GridItemCollection"),

            // Weapon → LootItemWeapon
            C("Weapon", [
                F("FireMode"),
                F("<Chambers>k__BackingField", "Chambers"),
                F("_magSlotCache"),
            ], cs: "LootItemWeapon"),

            // LevelSettings
            C("LevelSettings", [F("AmbientMode"), F("EquatorColor"), F("GroundColor")]),

            // SlotView_2 → PlayerBodySubclass
            C("SlotView_2", [F("Dresses")], cs: "PlayerBodySubclass"),

            // Dress
            C("Dress", [F("Renderers")]),

            // EFTHardSettings (singleton with TypeIndex)
            C("EFTHardSettings", [
                F("POSE_CHANGING_SPEED"),
                F("_instance"),
                F("MED_EFFECT_USING_PANEL"),
                F("MOUSE_LOOK_HORIZONTAL_LIMIT"),
                F("MOUSE_LOOK_LIMIT_IN_AIMING_COEF"),
                F("MOUSE_LOOK_VERTICAL_LIMIT"),
                F("ABOVE_OR_BELOW"),
                F("ABOVE_OR_BELOW_STAIRS"),
                F("AIM_PROCEDURAL_INTENSITY"),
                F("AIR_CONTROL_BACK_DIR"),
                F("AIR_CONTROL_NONE_OR_ORT_DIR"),
                F("AIR_CONTROL_SAME_DIR"),
                F("AIR_LERP"),
                F("AIR_MIN_SPEED"),
                F("DecelerationSpeed"),
                F("WEAPON_OCCLUSION_LAYERS"),
                F("DOOR_RAYCAST_DISTANCE"),
                F("LOOT_RAYCAST_DISTANCE"),
            ], s: true, ti: Offsets.Special.EFTHardSettings_TypeIndex),

            // GPUInstancerManager (singleton with TypeIndex)
            C("GPUInstancerManager", [
                F("runtimeDataList"),
            ], s: true, ti: Offsets.Special.GPUInstancerManager_TypeIndex),

            // ClientBackendSession
            C("ClientBackendSession", [F("<BackEndConfig>k__BackingField", "BackEndConfig")]),

            // FireModeComponent
            C("FireModeComponent", [F("FireMode")]),

            // MagazineTemplate → LootItemMagazine
            C("MagazineTemplate", [F("Cartridges"), F("LoadUnloadModifier")], cs: "LootItemMagazine"),

            // Item → MagazineClass
            C("Item", [F("StackObjectsCount")], cs: "MagazineClass"),

            // StackSlot
            C("StackSlot", [F("_items"), F("MaxCount")]),

            // ItemTemplate
            C("ItemTemplate", [F("Name"), F("ShortName"), F("<_id>k__BackingField", "_id"), F("Weight"), F("QuestItem")]),

            // ModTemplate
            C("ModTemplate", [F("Velocity")]),

            // AmmoTemplate
            C("AmmoTemplate", [
                F("InitialSpeed"), F("BallisticCoeficient"), F("BulletMassGram"), F("BulletDiameterMilimeters"),
            ]),

            // WeaponTemplate
            C("WeaponTemplate", [
                F("Velocity"), F("AllowJam"), F("AllowFeed"), F("AllowMisfire"), F("AllowSlide"),
            ]),

            // PlayerBody
            C("PlayerBody", [
                F("SkeletonRootJoint"), F("BodySkins"), F("_bodyRenderers"), F("SlotViews"), F("PointOfView"),
            ]),

            // InventoryBlur
            C("InventoryBlur", [F("_blurCount"), F("_upsampleTexDimension")]),

            // Physical
            C("PhysicalBase", [
                F("Overweight"), F("WalkOverweight"), F("WalkSpeedLimit"), F("Inertia"),
                F("Stamina"), F("Oxygen"), F("BaseOverweightLimits"), F("SprintOverweightLimits"),
                F("PreviousWeight"), F("SprintAcceleration"), F("PreSprintAcceleration"),
                F("_encumbered"), F("_overEncumbered"), F("SprintOverweight"), F("<BerserkRestorationFactor>k__BackingField", "BerserkRestorationFactor"),
            ], cs: "Physical"),

            // Stamina → PhysicalValue
            C("Stamina", [F("Current")], cs: "PhysicalValue"),

            // BreathEffector
            C("BreathEffector", [F("Intensity")]),

            // OpticCameraManager
            C("OpticCameraManager", [F("<Camera>k__BackingField", "Camera"), F("<CurrentOpticSight>k__BackingField", "CurrentOpticSight")]),

            // GPUInstancerRuntimeData
            C("GPUInstancerRuntimeData", [F("instanceBounds")]),

            // CameraManager → EFTCameraManager
            C("CameraManager", [
                F("<OpticCameraManager>k__BackingField", "OpticCameraManager"),
                F("<Camera>k__BackingField", "Camera"),
                M("get_Instance_RVA", "GetInstance_RVA"),
            ], cs: "EFTCameraManager"),

            // SightComponent
            C("SightComponent", [
                F("_template"), F("ScopesSelectedModes"), F("SelectedScope"), F("ScopeZoomValue"),
            ]),

            // SightModTemplate → SightInterface
            C("SightModTemplate", [F("Zooms")], cs: "SightInterface"),

            // WeatherController (instance fields + static Instance, with TypeIndex)
            C("WeatherController", [F("Instance"), F("WeatherDebug")], s: true, ti: Offsets.Special.WeatherController_TypeIndex),

            // WeatherDebug
            C("WeatherDebug", [
                F("CloudDensity"), F("Fog"), F("LightningThunderProbability"),
                F("Rain"), F("WindMagnitude"), F("isEnabled"),
            ]),

            // MatchingProgress — pure data model, no static singleton
            C("MatchingProgress", [
                F("StatusUpdateEvent"),
                F("MatchingProgressChangedEvent"),
                F("<CurrentStage>k__BackingField",                     "CurrentStage"),
                F("<CurrentStageGroup>k__BackingField",                "CurrentStageGroup"),
                F("<CurrentStageProgress>k__BackingField",             "CurrentStageProgress"),
                F("<EstimateTime>k__BackingField",                     "EstimateTime"),
                F("<StartTime>k__BackingField",                        "StartTime"),
                F("<IsAbortAvailable>k__BackingField",                 "IsAbortAvailable"),
                F("<BlockAbortAbilityDurationSeconds>k__BackingField", "BlockAbortAbilityDurationSeconds"),
                F("<ShowAbortConfirmationPopup>k__BackingField",       "ShowAbortConfirmationPopup"),
                F("<IsMatchingAbortRequested>k__BackingField",         "IsMatchingAbortRequested"),
                F("<CanProcessServerStages>k__BackingField",           "CanProcessServerStages"),
            ]),

            // MatchingProgressView — MonoBehaviour owner, resolved via GOM klass-pointer walk
            C("MatchingProgressView", [
                F("_matchingProgress"),
            ], ti: Offsets.Special.MatchingProgressView_TypeIndex),
        ];
    }
}
