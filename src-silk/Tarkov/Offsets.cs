#pragma warning disable IDE0130
#pragma warning disable CA2211

// ──────────────────────────────────────────────────────────────────────────────
// Game SDK offsets — fallback values overwritten at runtime by the IL2CPP dumper.
// The dumper resolves offsets via reflection on typeof(Offsets).
// ──────────────────────────────────────────────────────────────────────────────

namespace SDK
{
    public readonly partial struct Offsets
    {
        public readonly partial struct AssemblyCSharp
        {
            public static uint TypeStart = 0;
            public static uint TypeCount = 16336;
        }
        public readonly partial struct TarkovApplication
        {
            public static uint _menuOperation = 0x128;
        }
        public readonly partial struct MainMenuShowOperation
        {
            public static uint _afkMonitor = 0x38;
            public static uint _preloaderUI = 0x58;
            public static uint _profile = 0x48;
        }
        public readonly partial struct PreloaderUI
        {
            public static uint _sessionIdText = 0x118;
            public static uint _alphaVersionLabel = 0x20;
        }
        public readonly partial struct AfkMonitor
        {
            public static uint Delay = 0x10;
        }
        public readonly partial struct GameWorld
        {
            public static uint GameDateTime = 0xD8;
            public static uint SynchronizableObjectLogicProcessor = 0x248;
        }
        public readonly partial struct ClientLocalGameWorld
        {
            public static uint BtrController = 0x28;
            public static uint TransitController = 0x38;
            public static uint ExfilController = 0x58;
            public static uint ClientShellingController = 0xA8;
            public static uint LocationId = 0xD0;
            public static uint LootList = 0x198;
            public static uint RegisteredPlayers = 0x1B8;
            public static uint BorderZones = 0x1F0;
            public static uint MainPlayer = 0x210;
            public static uint World = 0x218;
            public static uint SynchronizableObjectLogicProcessor = 0x248;
            public static uint Grenades = 0x288;
        }
        public readonly partial struct TransitController
        {
            public static uint TransitPoints = 0x18;
        }
        public readonly partial struct ClientShellingController
        {
            public static uint ActiveClientProjectiles = 0x68;
        }
        public readonly partial struct WorldController
        {
            public static uint Interactables = 0x30;
        }
        public readonly partial struct Interactable
        {
            public static uint KeyId = 0x60;
            public static uint Id = 0x70;
            public static uint _doorState = 0xD0;
        }
        public readonly partial struct ArtilleryProjectileClient
        {
            public const uint Position = 0x30;
            public const uint IsActive = 0x3C;
        }
        public readonly partial struct TransitPoint
        {
            public static uint parameters = 0x20;
        }
        public readonly partial struct TransitParameters
        {
            public static uint id = 0x10;
            public static uint active = 0x14;
            public static uint name = 0x18;
            public static uint description = 0x20;
            public static uint target = 0x38;
            public static uint location = 0x40;
        }
        public readonly partial struct SynchronizableObject
        {
            public static uint Type = 0x68;
        }
        public readonly partial struct SynchronizableObjectLogicProcessor
        {
            // Resolved at runtime from IL2CPP field `_staticSynchronizableObjects`
            // (see Il2CppDumperSchema). The C# name is kept as `_activeSynchronizableObjects`
            // for backwards compatibility with existing call sites. If tripwires ever stop
            // displaying after a game update, delete the AppData cache
            // (%AppData%\eft-dma-radar-silk\il2cpp_offsets.json) and restart to force a
            // fresh dump — or override this key in the cache.
            public static uint _activeSynchronizableObjects = 0x18;
        }
        public readonly partial struct TripwireSynchronizableObject
        {
            public static uint GrenadeTemplateId = 0x118;
            public static uint _tripwireState = 0xE4;
            public static uint FromPosition = 0x14C;
            public static uint ToPosition = 0x158;
        }
        public readonly partial struct BtrController
        {
            public static uint BtrView = 0x50;
        }
        public readonly partial struct BTRView
        {
            public static uint turret = 0x60;
            public static uint _previousPosition = 0xB4;
        }
        public readonly partial struct BTRTurretView
        {
            public static uint AttachedBot = 0x60;
        }
        public readonly partial struct EffectsController
        {
            public static uint _effectsPrefab = 0x20;
            public static uint FastVineteFlicker = 0x28;
            public static uint RainScreenDrops = 0x30;
            public static uint ScreenWater = 0x38;
            public static uint _vignette = 0x40;
            public static uint _doubleVision = 0x48;
            public static uint _hueFocus = 0x50;
            public static uint _radialBlur = 0x58;
            public static uint _sharpen = 0x60;
            public static uint _lowhHealthBlend = 0x68;
            public static uint _bloodlossBlend = 0x70;
            public static uint _wiggle = 0x78;
            public static uint _motionBluer = 0x80;
            public static uint _bloodOnScreen = 0x88;
            public static uint _grenadeFlash = 0x90;
            public static uint _eyeBurn = 0x98;
            public static uint _blur = 0xA0;
            public static uint _dof = 0xA8;
            public static uint _effectAccumulators = 0xB0;
            public static uint _sharpenAccumulator = 0xB8;
            public static uint _radialBlurAccumulator = 0xC0;
            public static uint _chromaticAberration = 0xC8;
            public static uint _thermalVision = 0xD0;
            public static uint _frostbiteEffect = 0xD8;
        }
        public readonly partial struct FrostbiteEffect
        {
            public static uint _opacity = 0x64;
        }
        public readonly partial struct NightVision
        {
            public static uint _on = 0xC4;
        }
        public readonly partial struct ThermalVision
        {
            public static uint Material = 0xB8;
            public static uint On = 0x20;
            public static uint IsNoisy = 0x21;
            public static uint IsFpsStuck = 0x22;
            public static uint IsMotionBlurred = 0x23;
            public static uint IsGlitch = 0x24;
            public static uint IsPixelated = 0x25;
            public static uint ChromaticAberrationThermalShift = 0x68;
            public static uint UnsharpRadiusBlur = 0x90;
            public static uint UnsharpBias = 0x94;
        }
        public readonly partial struct HealthController
        {
            public static uint Energy = 0x68;
            public static uint Hydration = 0x70;
        }
        public readonly partial struct ExfilController
        {
            public static uint ExfiltrationPointArray = 0x20;
            public static uint ScavExfiltrationPointArray = 0x28;
            public static uint SecretExfiltrationPointArray = 0x30;
        }
        public readonly partial struct Exfil
        {
            public static uint _status = 0x58;
            public static uint Settings = 0x98;
            public static uint EligibleEntryPoints = 0xC0;
        }
        public readonly partial struct ScavExfil
        {
            public static uint EligibleIds = 0xF8;
        }
        public readonly partial struct ExfilSettings
        {
            public static uint Name = 0x18;
        }
        public readonly partial struct Grenade
        {
            public static uint IsDestroyed = 0x4D;
            public static uint WeaponSource = 0x98;
        }
        public readonly partial struct Player
        {
            public static uint _characterController = 0x40;
            public static uint MovementContext = 0x60;
            public static uint _playerBody = 0x190;
            public static uint ProceduralWeaponAnimation = 0x338;
            public static uint _animators = 0x648;
            public static uint EnabledAnimators = 0x678;
            public static uint Corpse = 0x688;
            public static uint Location = 0x878;
            public static uint InteractableObject = 0x890;
            public static uint Profile = 0x908;
            public static uint Physical = 0x920;
            public static uint AIData = 0x948;
            public static uint _healthController = 0x968;
            public static uint _inventoryController = 0x980;
            public static uint _handsController = 0x988;
            public static uint _playerLookRaycastTransform = 0xA18;
            public static uint InteractionRayOriginOnStartOperation = 0xA24;
            public static uint InteractionRayDirectionOnStartOperation = 0xA30;
            public static uint IsYourPlayer = 0xA91;
            public static uint VoipID = 0x8F8;
            public static uint Id = 0x900;
            public static uint GameWorld = 0x600;
        }
        public readonly partial struct ObservedPlayerView
        {
            public static uint ObservedPlayerController = 0x28;
            public static uint Voice = 0x40;
            public static uint VisibleToCameraType = 0x60;
            public static uint GroupID = 0x80;
            public static uint Side = 0x94;
            public static uint IsAI = 0xA0;
            public static uint NickName = 0xB8;
            public static uint AccountId = 0xC0;
            public static uint PlayerBody = 0xD8;
            public static uint Id = 0x7C;
            public static uint VoipId = 0xB0;
            public static uint _playerLookRaycastTransform = 0x100;
        }
        public readonly partial struct ObservedPlayerController
        {
            public static uint InventoryController = 0x10;
            public static uint Player = 0x18;
            public static uint InfoContainer = 0xD0;
            public static readonly uint[] MovementController = [0xD8, 0x98];
            public static uint HealthController = 0xE8;
            public static uint HandsController = 0x120;
        }
        public readonly partial struct ObservedMovementController
        {
            public static uint Rotation = 0x28;
            public static uint Velocity = 0xF8;
        }
        public readonly partial struct ObservedHandsController
        {
            public static uint ItemInHands = 0x58;
            public static uint BundleAnimationBones = 0xA8;
        }
        public readonly partial struct BundleAnimationBonesController
        {
            public static uint ProceduralWeaponAnimationObs = 0xD0;
        }
        public readonly partial struct ProceduralWeaponAnimationObs
        {
            public static uint _isAimingObs = 0x14D;
        }
        public readonly partial struct ObservedHealthController
        {
            public static uint Player = 0x18;
            public static uint PlayerCorpse = 0x20;
            public static uint HealthStatus = 0x10;
        }
        public readonly partial struct ProceduralWeaponAnimation
        {
            public static uint ShotNeedsFovAdjustments = 0x46B;
            public static uint Breath = 0x38;
            public static uint PositionZeroSum = 0x348;
            public static uint Shootingg = 0x58;
            public static uint _aimingSpeed = 0x190;
            public static uint _isAiming = 0x14D;
            public static uint _optics = 0x1A8;
            public static uint _shotDirection = 0x1F0;
            public static uint Mask = 0x30;
            public static uint HandsContainer = 0x20;
            public static uint _fovCompensatoryDistance = 0x1BC;
        }
        public readonly partial struct HandsContainer
        {
            public static uint CameraOffset = 0xDC;
            public static uint HandsRotation = 0x40;
            public static uint CameraRotation = 0x48;
            public static uint CameraPosition = 0x50;
        }
        public readonly partial struct SightNBone
        {
            public static uint Mod = 0x10;
        }
        public readonly partial struct ShotEffector
        {
            public static uint NewShotRecoil = 0x20;
        }
        public readonly partial struct PlayerStateContainer
        {
            public static uint Name = 0x19;
            public static uint StateFullNameHash = 0x40;
        }
        public readonly partial struct NewShotRecoil
        {
            public static uint IntensitySeparateFactors = 0x94;
        }
        public readonly partial struct VisorEffect
        {
            public static uint Intensity = 0x20;
        }
        public readonly partial struct TOD_Time
        {
            public static uint LockCurrentTime = 0x20;
        }
        public readonly partial struct TOD_CycleParameters
        {
            public static uint Hour = 0x10;
        }
        public readonly partial struct TOD_Scattering
        {
            public static uint Sky = 0x28;
        }
        public readonly partial struct TOD_Sky
        {
            public static uint Cycle = 0x38;
            public static uint TOD_Components = 0xA0;
        }
        public readonly partial struct TOD_Components
        {
            public static uint TOD_Time = 0x118;
        }
        public readonly partial struct Profile
        {
            public static uint Id = 0x10;
            public static uint AccountId = 0x18;
            public static uint Info = 0x48;
            public static uint Inventory = 0x70;
            public static uint Skills = 0x80;
            public static uint TaskConditionCounters = 0x90;
            public static uint QuestsData = 0x98;
            public static uint WishlistManager = 0x108;
            public static uint Stats = 0x148;
        }
        public readonly partial struct WishlistManager
        {
            public static uint Items = 0x28;
        }
        public readonly partial struct PlayerInfo
        {
            public static uint Nickname = 0x10;
            public static uint EntryPoint = 0x28;
            public static uint Side = 0x48;
            public static uint RegistrationDate = 0x4C;
            public static uint GroupId = 0x50;
            public static uint Settings = 0x78;
            public static uint MemberCategory = 0x80;
            public static uint Experience = 0x84;
        }
        public readonly partial struct SkillManager
        {
            public static uint StrengthBuffJumpHeightInc = 0x60;
            public static uint StrengthBuffThrowDistanceInc = 0x70;
            public static uint MagDrillsLoadSpeed = 0x180;
            public static uint MagDrillsUnloadSpeed = 0x188;
            public static uint RaidLoadedAmmoAction = 0x480;
            public static uint RaidUnloadedAmmoAction = 0x488;
        }
        public readonly partial struct SkillValueContainer
        {
            public static uint Value = 0x30;
        }
        public readonly partial struct QuestData
        {
            public static uint Id = 0x10;
            public static uint Status = 0x1C;
            public static uint CompletedConditions = 0x28;
            public static uint Template = 0x38;
        }
        public readonly partial struct CompletedConditionsCollection
        {
            public static uint BackendData = 0x10;
            public static uint LocalChanges = 0x18;
        }
        public readonly partial struct QuestTemplate
        {
            public static uint Conditions = 0x60;
            public static uint Name = 0xC8;
        }
        public readonly partial struct QuestConditionsContainer
        {
            public static uint ConditionsList = 0x70;
        }
        public readonly partial struct ItemHandsController
        {
            public static uint Item = 0x70;
        }
        public readonly partial struct FirearmController
        {
            public static uint Fireport = 0x150;
            public static uint TotalCenterOfImpact = 0xF0;
            public static uint WeaponLn = 0x100;
        }
        public readonly partial struct ClientFirearmController
        {
            public static uint WeaponLn = 0x100;
            public static uint ShotIndex = 0x438;
        }
        public readonly partial struct MovementContext
        {
            public static uint Player = 0x40;
            public static uint _rotation = 0xC0;
            public static uint PlantState = 0x70;
            public static uint CurrentState = 0x1E8;
            public static uint _states = 0x478;
            public static uint _movementStates = 0x4A8;
            public static uint _tilt = 0xAC;
            public static uint _physicalCondition = 0x190;
            public static uint _speedLimitIsDirty = 0x1B1;
            public static uint StateSpeedLimit = 0x1B4;
            public static uint StateSprintSpeedLimit = 0x1B8;
            public static uint _lookDirection = 0x3B0;
            public static uint WalkInertia = 0x4B4;
            public static uint SprintBrakeInertia = 0x4B8;
            public static uint _poseInertia = 0x4BC;
            public static uint _currentPoseInertia = 0x4C0;
            public static uint _inertiaAppliedTime = 0x264;
        }
        public readonly partial struct MovementState
        {
            public static uint StickToGround = 0x54;
            public static uint PlantTime = 0x58;
            public static uint Name = 0x11;
            public static uint AnimatorStateHash = 0x20;
            public static uint _velocity = 0xDC;
            public static uint _velocity2 = 0xE4;
            public static uint AuthoritySpeed = 0x28;
        }
        public readonly partial struct InventoryController
        {
            public static uint Inventory = 0x100;
        }
        public readonly partial struct Inventory
        {
            public static uint Equipment = 0x18;
            public static uint QuestRaidItems = 0x28;
            public static uint QuestStashItems = 0x30;
            public static uint Stash = 0x20;
        }
        public readonly partial struct Stash
        {
            public static uint Grids = 0x98;
            public static uint Slots = 0x80;
        }
        public readonly partial struct Equipment
        {
            public static uint Grids = 0x78;
            public static uint Slots = 0x80;
        }
        public readonly partial struct BarterOtherOffsets
        {
            public static uint Dogtag = 0x80;
        }
        public readonly partial struct DogtagComponent
        {
            public static uint GroupId = 0x18;
            public static uint AccountId = 0x20;
            public static uint ProfileId = 0x28;
            public static uint Nickname = 0x30;
            public static uint Side = 0x38;
            public static uint Level = 0x3c;
            public static uint Time = 0x40;
            public static uint Status = 0x48;
            public static uint KillerAccountId = 0x50;
            public static uint KillerProfileId = 0x58;
            public static uint KillerName = 0x60;
            public static uint WeaponName = 0x68;
            public static uint CarriedByGroupMember = 0x70;
        }
        public readonly partial struct Grids
        {
            public static uint ContainedItems = 0x48;
        }
        public readonly partial struct GridContainedItems
        {
            public static uint Items = 0x18;
        }
        public readonly partial struct Slot
        {
            public static uint ContainedItem = 0x48;
            public static uint ID = 0x58;
            public static uint Required = 0x18;
        }
        public readonly partial struct InteractiveLootItem
        {
            public static uint Item = 0xF0;
        }
        public readonly partial struct DizSkinningSkeleton
        {
            public static uint _values = 0x30;
        }
        public readonly partial struct LootableContainer
        {
            public static uint InteractingPlayer = 0x150;
            public static uint ItemOwner = 0x168;
            public static uint Template = 0x170;
        }
        public readonly partial struct LootableContainerItemOwner
        {
            public static uint RootItem = 0xD0;
        }
        public readonly partial struct LootItem
        {
            public static uint StackObjectsCount = 0x24;
            public static uint Version = 0x28;
            public static uint Components = 0x40;
            public static uint Template = 0x60;
            public static uint SpawnedInSession = 0x68;
        }
        public readonly partial struct LootItemMod
        {
            public static uint Grids = 0x78;
            public static uint Slots = 0x80;
        }
        public readonly partial struct Grid
        {
            public static uint ItemCollection = 0x48;
        }
        public readonly partial struct GridItemCollection
        {
            public static uint ItemsList = 0x18;
        }
        public readonly partial struct LootItemWeapon
        {
            public static uint FireMode = 0xA0;
            public static uint Chambers = 0xB0;
            public static uint _magSlotCache = 0xC8;
        }
        public readonly partial struct LevelSettings
        {
            public static uint AmbientMode = 0x60;
            public static uint EquatorColor = 0x74;
            public static uint GroundColor = 0x84;
        }
        public readonly partial struct PlayerBodySubclass
        {
            public static uint Dresses = 0x40;
        }
        public readonly partial struct Dress
        {
            public static uint Renderers = 0x38;
        }
        public readonly partial struct SlotViewsContainer
        {
            public static uint Dict = 0x10;
        }
        public readonly partial struct EFTHardSettings
        {
            public static uint POSE_CHANGING_SPEED = 0x380;
            public static uint _instance = 0x0;
            public static uint MED_EFFECT_USING_PANEL = 0x3B4;
            public static uint MOUSE_LOOK_HORIZONTAL_LIMIT = 0x340;
            public static uint MOUSE_LOOK_LIMIT_IN_AIMING_COEF = 0x350;
            public static uint MOUSE_LOOK_VERTICAL_LIMIT = 0x348;
            public static uint ABOVE_OR_BELOW = 0x204;
            public static uint ABOVE_OR_BELOW_STAIRS = 0x20C;
            public static uint AIM_PROCEDURAL_INTENSITY = 0x3FC;
            public static uint AIR_CONTROL_BACK_DIR = 0x15C;
            public static uint AIR_CONTROL_NONE_OR_ORT_DIR = 0x160;
            public static uint AIR_CONTROL_SAME_DIR = 0x158;
            public static uint AIR_LERP = 0x3AC;
            public static uint AIR_MIN_SPEED = 0x3A8;
            public static uint DecelerationSpeed = 0x50;
            public static uint WEAPON_OCCLUSION_LAYERS = 0x238;
            public static uint DOOR_RAYCAST_DISTANCE = 0x18C;
            public static uint LOOT_RAYCAST_DISTANCE = 0x188;
        }
        public readonly partial struct EftScreenManager
        {
            public static uint _instance = 0x0;
        }
        public readonly partial struct GPUInstancerManager
        {
            public static uint Instance = 0x0;
            public static uint runtimeDataList = 0x58;
        }
        public readonly partial struct ClientBackendSession
        {
            public static uint BackEndConfig = 0x158;
        }
        public readonly partial struct BSGGameSettingValueClass
        {
            public static uint Value = 0x30;
        }
        public readonly partial struct BSGGameSetting
        {
            public static uint ValueClass = 0x28;
        }
        public readonly partial struct FireModeComponent
        {
            public static uint FireMode = 0x28;
        }
        public readonly partial struct LootItemMagazine
        {
            public static uint Cartridges = 0x1A8;
            public static uint LoadUnloadModifier = 0x1B0;
        }
        public readonly partial struct MagazineClass
        {
            public static uint StackObjectsCount = 0x24;
        }
        public readonly partial struct StackSlot
        {
            public static uint _items = 0x18;
            public static uint MaxCount = 0x10;
        }
        public readonly partial struct ItemTemplate
        {
            public static uint Name = 0x10;
            public static uint ShortName = 0x18;
            public static uint _id = 0xE0;
            public static uint Weight = 0x28;
            public static uint QuestItem = 0x34;
        }
        public readonly partial struct ModTemplate
        {
            public static uint Velocity = 0x188;
        }
        public readonly partial struct AmmoTemplate
        {
            public static uint InitialSpeed = 0x1A4;
            public static uint BallisticCoeficient = 0x1B8;
            public static uint BulletMassGram = 0x25C;
            public static uint BulletDiameterMilimeters = 0x260;
        }
        public readonly partial struct WeaponTemplate
        {
            public static uint Velocity = 0x25C;
            public static uint AllowJam = 0x310;
            public static uint AllowFeed = 0x311;
            public static uint AllowMisfire = 0x312;
            public static uint AllowSlide = 0x313;
        }
        public readonly partial struct PlayerBody
        {
            public static uint SkeletonRootJoint = 0x30;
            public static uint BodySkins = 0x58;
            public static uint _bodyRenderers = 0x68;
            public static uint SlotViews = 0x90;
            public static uint PointOfView = 0xC0;
        }
        public readonly partial struct InventoryBlur
        {
            public static uint _blurCount = 0x38;
            public static uint _upsampleTexDimension = 0x30;
        }
        public readonly partial struct Physical
        {
            public static uint Overweight = 0x1C;
            public static uint WalkOverweight = 0x20;
            public static uint WalkSpeedLimit = 0x24;
            public static uint Inertia = 0x28;
            public static uint Stamina = 0x68;
            public static uint Oxygen = 0x78;
            public static uint BaseOverweightLimits = 0xAC;
            public static uint SprintOverweightLimits = 0xC0;
            public static uint PreviousWeight = 0xD4;
            public static uint SprintAcceleration = 0x114;
            public static uint PreSprintAcceleration = 0x118;
            public static uint _encumbered = 0x11C;
            public static uint _overEncumbered = 0x11D;
            public static uint SprintOverweight = 0xD0;
            public static uint BerserkRestorationFactor = 0x110;
        }
        public readonly partial struct PhysicalValue
        {
            public static uint Current = 0x10;
        }
        public readonly partial struct BreathEffector
        {
            public static uint Intensity = 0x30;
        }
        public readonly partial struct OpticCameraManager
        {
            public static uint Camera = 0x70;
            public static uint CurrentOpticSight = 0x78;
        }
        public readonly partial struct GPUInstancerRuntimeData
        {
            public static uint instanceBounds = 0x20;
        }
        public readonly partial struct EFTCameraManager
        {
            public static uint OpticCameraManager = 0x10;
            public static uint Camera = 0x60;
            public static uint GetInstance_RVA = 0x3F151A0;
        }
        public readonly partial struct SightComponent
        {
            public const uint _template = 0x20;
            public const uint ScopesSelectedModes = 0x30;
            public const uint SelectedScope = 0x38;
            public const uint ScopeZoomValue = 0x3C;
        }
        public readonly partial struct SightInterface
        {
            public const uint Zooms = 0x1B8;
        }
        public readonly partial struct WeatherController
        {
            public static uint Instance = 0x0;
            public static uint WeatherDebug = 0x88;
        }
        public readonly partial struct WeatherDebug
        {
            public static uint CloudDensity = 0x24;
            public static uint Fog = 0x28;
            public static uint LightningThunderProbability = 0x30;
            public static uint Rain = 0x2c;
            public static uint WindMagnitude = 0x14;
            public static uint isEnabled = 0x10;
        }
        public readonly partial struct Special
        {
            public static ulong TypeInfoTableRva = 0x5ACCD98;
            public static uint EFTHardSettings_TypeIndex = 225;
            public static uint GPUInstancerManager_TypeIndex = 4920;
            public static uint WeatherController_TypeIndex = 10112;
            public static uint GlobalConfiguration_TypeIndex = 6409;
            public static uint MatchingProgress_TypeIndex = 15360;
            public static uint MatchingProgressView_TypeIndex = 15363;
            public static uint GamePlayerOwner_TypeIndex = 8574;
            public static uint TarkovApplication_TypeIndex = 7967;
            public static uint HideoutArea_TypeIndex = 9178;
            public static uint HideoutController_TypeIndex = 9189;
        }
        public readonly partial struct MatchingProgress
        {
            public const uint StatusUpdateEvent = 0x10;
            public const uint MatchingProgressChangedEvent = 0x18;
            public const uint CurrentStage = 0x20;
            public const uint CurrentStageGroup = 0x24;
            public const uint CurrentStageProgress = 0x28;
            public const uint EstimateTime = 0x30;
            public const uint StartTime = 0x38;
            public const uint IsAbortAvailable = 0x40;
            public const uint BlockAbortAbilityDurationSeconds = 0x44;
            public const uint ShowAbortConfirmationPopup = 0x48;
            public const uint IsMatchingAbortRequested = 0x49;
            public const uint LastMemorizedDelayedStage = 0x4C;
            public const uint LastMemorizedDelayedStageProgress = 0x54;
            public const uint CanProcessServerStages = 0x5C;
        }
        public readonly partial struct MatchingProgressView
        {
            public const uint _matchingProgress = 0x130;
            public const uint _lastUpdateTime = 0x138;
            public const uint _matchingWarningType = 0x148;
            public const uint _matchingWarningType_hasValue = 0x14C;
            public const uint _serversLimited = 0x160;
            public const uint _canUpdateStatus = 0x161;
            public const uint _maxMatchingTimeInSeconds = 0x164;
        }
        public readonly partial struct GamePlayerOwner
        {
            public static uint _myPlayer = 0x8;
        }

        public readonly partial struct Il2CppClass
        {
            public const uint Name = 0x10;
            public const uint Namespace = 0x18;
            public const uint Parent = 0x58;
            public const uint Fields = 0x80;
            public const uint StaticFields = 0xB8;
            public const uint Methods = 0x98;
            public const uint MethodCount = 0x120;
            public const uint FieldCount = 0x124;
        }

        // ── SDK_Manual additions ────────────────────────────────────────────

        public readonly struct BodyAnimator
        {
            public const uint UnityAnimator = 0x10;
        }

        public readonly partial struct HealthSystem
        {
            public const uint Energy = 0x38;
            public const uint Hydration = 0x40;
        }

        public readonly partial struct HealthValue
        {
            public static uint Value = 0x10;
        }

        public readonly partial struct TaskConditionCounter
        {
            public const uint Value = 0x40;
        }
    }

    // ── Types (memory layout structs) ───────────────────────────────────────

    public readonly struct Types
    {
        /// <summary>
        /// EFT.MongoID Struct
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Pack = 8)]
        public readonly struct MongoID
        {
            [FieldOffset(0x0)]
            private readonly uint _timeStamp;
            [FieldOffset(0x8)]
            private readonly ulong _counter;
            [FieldOffset(0x10)]
            private readonly ulong _stringID;

            public readonly ulong StringID => _stringID;
        }

        /// <summary>
        /// EFT.HealthSystem.Value Struct
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Pack = 8)]
        public readonly struct HealthSystem
        {
            [FieldOffset(0x0)]
            private readonly float _current;
            [FieldOffset(0x04)]
            private readonly float _maximum;
            [FieldOffset(0x08)]
            private readonly float _minimum;
            [FieldOffset(0x0C)]
            private readonly float _overDamageReceivedMultiplier;
            [FieldOffset(0x10)]
            private readonly float _environmentDamageMultiplier;

            public readonly float Current => _current;
        }

        /// <summary>
        /// Check _bodyRenderers type to see if struct changed.
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        public readonly struct BodyRendererContainer
        {
            [FieldOffset(0x0)]
            private readonly int DecalType;
            [FieldOffset(0x8)]
            public readonly ulong Renderers;
        }
    }

    // ── Game enums ──────────────────────────────────────────────────────────

    public enum SynchronizableObjectType
    {
        AirDrop = 0,
        AirPlane = 1,
        Tripwire = 2,
    }

    public enum ETripwireState
    {
        None = 0,
        Wait = 1,
        Active = 2,
        Exploding = 3,
        Exploded = 4,
        Inert = 5,
    }

    // ── Matching / Loading progress enums ────────────────────────────────────

    public enum EMatchingStage
    {
        None = 0,
        GameWorldCreating = 1,
        BundlesLoading = 2,
        PoolsCreating = 3,
        MapLoading = 4,
        DataCaching = 5,
        PlayersSearching = 6,
        ServerSearching = 7,
        ServerStartAwaiting = 8,
        GamePreparing = 9,
        ServerConnecting = 10,
        ServerResponseAwaiting = 11,
        LootBundlesLoading = 12,
        LootPoolsCreating = 13,
        SessionStartAwaiting = 14,
        LocalGameStarting = 15,
        PlayersAwaiting = 16,
        SynchronizationWithOtherPlayers = 17,
        GameLeaving = 18,
    }

    public enum EMatchingStageGroup
    {
        GameWorldCreating = 0,
        MapLoading = 1,
        PlayersSearching = 2,
        ServerSearching = 3,
        ServerStartAwaiting = 4,
        ServerConnecting = 5,
        LootLoading = 6,
        SessionStartAwaiting = 7,
        PlayersAwaiting = 8,
    }

    public static class EMatchingStageExtensions
    {
        public static string ToDisplayString(this EMatchingStage stage) => stage switch
        {
            EMatchingStage.GameWorldCreating => "Creating Game World",
            EMatchingStage.BundlesLoading => "Loading Bundles",
            EMatchingStage.PoolsCreating => "Creating Pools",
            EMatchingStage.MapLoading => "Loading Map",
            EMatchingStage.DataCaching => "Caching Data",
            EMatchingStage.PlayersSearching => "Searching for Players",
            EMatchingStage.ServerSearching => "Searching for Server",
            EMatchingStage.ServerStartAwaiting => "Awaiting Server Start",
            EMatchingStage.GamePreparing => "Preparing Game",
            EMatchingStage.ServerConnecting => "Connecting to Server",
            EMatchingStage.ServerResponseAwaiting => "Awaiting Server Response",
            EMatchingStage.LootBundlesLoading => "Loading Loot Bundles",
            EMatchingStage.LootPoolsCreating => "Creating Loot Pools",
            EMatchingStage.SessionStartAwaiting => "Awaiting Session Start",
            EMatchingStage.LocalGameStarting => "Starting Game",
            EMatchingStage.PlayersAwaiting => "Awaiting Players",
            EMatchingStage.SynchronizationWithOtherPlayers => "Synchronizing Players",
            EMatchingStage.GameLeaving => "Leaving Game",
            _ => $"Stage {(int)stage}",
        };
    }
}
