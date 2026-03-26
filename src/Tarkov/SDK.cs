#pragma warning disable CA2211 // Non-constant fields should not be visible

namespace SDK
{
	public readonly partial struct Offsets
	{
    	public readonly partial struct AssemblyCSharp
    	{
    	    public static uint TypeStart = 0;
    	    public static uint TypeCount = 16336;
    	}
		//public static class TarkovApplication
		public readonly partial struct TarkovApplication 
		{
			public static uint _menuOperation = 0x128;
		}
		//public static class MainMenuShowOperation
		public readonly partial struct MainMenuShowOperation
		{
			public static uint _afkMonitor = 0x38;
			public static uint _preloaderUI = 0x60;
			public static uint _profile = 0x50;
		}
		//public static class PreloaderUI
		public readonly partial struct PreloaderUI
		{
			public static uint _sessionIdText = 0x118;
			public static uint _alphaVersionLabel = 0x20;
		}
		//public static class AFKMonitor
		public readonly partial struct AfkMonitor
		{
			public static uint Delay = 0x10; //_afkTimeout
		}
		//public static class GameWorld
		public readonly partial struct GameWorld
		{
			public static uint GameDateTime = 0xD8;
			public static uint SynchronizableObjectLogicProcessor = 0x248; //_SynchronizableObjectLogicProcessor_k__BackingField
		}
		//public static class GameWorld
		public readonly partial struct ClientLocalGameWorld
		{
            public static uint BtrController = 0x28; //_BtrController_k__BackingField
            public static uint TransitController = 0x38; //_TransitController_k__BackingField
            public static uint ExfilController = 0x58; //_ExfiltrationController_k__BackingField
            public static uint ClientShellingController = 0xA8; //_ClientShellingController_k__BackingField
            public static uint LocationId = 0xD0; //_LocationId_k__BackingField
            public static uint LootList = 0x198;
            public static uint RegisteredPlayers = 0x1B8;
            public static uint BorderZones = 0x1F0;
            public static uint MainPlayer = 0x210;
            public static uint World = 0x218; //_world
            public static uint SynchronizableObjectLogicProcessor = 0x248; //_SynchronizableObjectLogicProcessor_k__BackingField
            public static uint Grenades = 0x288;
        }
		//public static class TransitController
		public readonly partial struct TransitController
		{
			public static uint TransitPoints = 0x18; //pointsById
		}
		//public static class ArtilleryShellingControllerClient
		public readonly partial struct ClientShellingController
		{
			public static uint ActiveClientProjectiles = 0x68;
		}
		//public static class World_2
		public readonly partial struct WorldController
		{
			public static uint Interactables = 0x30; //_interactables
		}
		//public static class WorldInteractiveObject
		public readonly partial struct Interactable
		{
			public static uint KeyId = 0x60;
			public static uint Id = 0x70;
			public static uint _doorState = 0xD0;
		}
		//public static class ArtilleryProjectileClient
		public readonly partial struct ArtilleryProjectileClient
		{
			public const uint Position = 0x30; //_targetPosition
			public const uint IsActive = 0x3C; //_flyOn
		}
		//public static class TransitPoint
		public readonly partial struct TransitPoint
		{
			public static uint parameters = 0x20;
		}
		//public static class TransitParameters
		public readonly partial struct TransitParameters
		{
			public static uint id = 0x10;
			public static uint active = 0x14;
			public static uint name = 0x18;
			public static uint description = 0x20;
			public static uint target = 0x38;
			public static uint location = 0x40;
		}
		//public static class SynchronizableObject
		public readonly partial struct SynchronizableObject
		{
			public static uint Type = 0x68;
		}
		//public static class SynchronizableObjectLogicProcessor
		public readonly partial struct SynchronizableObjectLogicProcessor
		{
			public static uint _activeSynchronizableObjects = 0x18;
		}
		//public static class TripwireSynchronizableObject
		public readonly partial struct TripwireSynchronizableObject
		{
			public static uint GrenadeTemplateId = 0x118; //_GrenadeTemplateId_k__BackingField
			public static uint _tripwireState = 0xE4;
			public static uint FromPosition = 0x14C; //_FromPosition_k__BackingField
			public static uint ToPosition = 0x158; //_ToPosition_k__BackingField
		}
		//public static class BtrController
		public readonly partial struct BtrController
		{
			public static uint BtrView = 0x50; //_BtrView_k__BackingField
		}
		//public static class BTRView
		public readonly partial struct BTRView
		{
			public static uint turret = 0x60;
			public static uint _previousPosition = 0xB4; //_previousPosition
		}
		//public static class BTRTurretView
		public readonly partial struct BTRTurretView
		{
			public static uint AttachedBot = 0x60; //_bot
		}
		//public static class EffectsController
		public readonly partial struct EffectsController
		{
			public static uint _effectsPrefab = 0x20;
            public static uint FastVineteFlicker = 0x28;
            public static uint RainScreenDrops = 0x30; //_RainScreenDrops_k__BackingField
            public static uint ScreenWater = 0x38; //_ScreenWater_k__BackingField
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
		//public static class FrostbiteEffect
		public readonly partial struct FrostbiteEffect
		{
			public static uint _opacity = 0x64;
		}
		//public static class NightVision
		public readonly partial struct NightVision
		{
			public static uint _on = 0xC4;
		}
		//public static class ThermalVision
		public readonly partial struct ThermalVision
		{
			public static uint Material = 0xB8; //_material
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
		//public static class HealthInfo
		public readonly partial struct HealthController
		{
			public static uint Energy = 0x68;
			public static uint Hydration = 0x70;
		}
		//public static class ExfiltrationController
		public readonly partial struct ExfilController
		{
			public static uint ExfiltrationPointArray = 0x20; //_ExfiltrationPoints_k__BackingField
			public static uint ScavExfiltrationPointArray = 0x28; //_ScavExfiltrationPoints_k__BackingField
			public static uint SecretExfiltrationPointArray = 0x30; //_SecretExfiltrationPoints_k__BackingField
		}
		//public static class ExfiltrationPoint
		public readonly partial struct Exfil
		{
			public static uint _status = 0x58;
			public static uint Settings = 0x98;
			public static uint EligibleEntryPoints = 0xC0;
		}
		//public static class ScavExfiltrationPoint
		public readonly partial struct ScavExfil
		{
			public static uint EligibleIds = 0xF8;
		}
		//public static class ExitTriggerSettings
		public readonly partial struct ExfilSettings
		{
			public static uint Name = 0x18;
		}
		//public static class Grenade + public static class Throwable
		public readonly partial struct Grenade
		{
			public static uint IsDestroyed = 0x4D; //_isDestroyed from Throwable
			public static uint WeaponSource = 0x98; //_WeaponSource_k__BackingField from Grenade
		}
		//public static class Player
		public readonly partial struct Player
		{
			public static uint _characterController = 0x40;
			public static uint MovementContext = 0x60; //_MovementContext_k__BackingField
			public static uint _playerBody = 0x190;
			public static uint ProceduralWeaponAnimation = 0x338; //_ProceduralWeaponAnimation_k__BackingField
			public static uint _animators = 0x640;
			public static uint EnabledAnimators = 0x670;
			public static uint Corpse = 0x680;
			public static uint Location = 0x870; //_Location_k__BackingField
			public static uint InteractableObject = 0x888; //_InteractableObject_k__BackingField
			public static uint Profile = 0x900; //_Profile_k__BackingField
			public static uint Physical = 0x918;
			public static uint AIData = 0x940; //_AIData_k__BackingField
			public static uint _healthController = 0x960;
			public static uint _inventoryController = 0x978;
			public static uint _handsController = 0x980;
			public static uint InteractionRayOriginOnStartOperation = 0xA1C; //_InteractionRayOriginOnStartOperation_k__BackingField
			public static uint InteractionRayDirectionOnStartOperation = 0xA28; //_InteractionRayDirectionOnStartOperation_k__BackingField
			public static uint IsYourPlayer = 0xA89; //_IsYourPlayer_k__BackingField
			public static uint VoipID = 0x8F0; //_VoipID_k__BackingField
			public static uint Id = 0x8F8;  //_PlayerId_k__BackingField
			public static uint GameWorld = 0x5F8; //_GameWorld_k__BackingField
		}
		//public static class ObservedPlayerView
		public readonly partial struct ObservedPlayerView
		{
			public static uint ObservedPlayerController = 0x28; //_ObservedPlayerController_k__BackingField
			public static uint Voice = 0x40; //_Voice_k__BackingField
			public static uint VisibleToCameraType = 0x60; //_VisibleToCameraType_k__BackingField
			public static uint GroupID = 0x80; //_GroupId_k__BackingField
			public static uint Side = 0x94; //_Side_k__BackingField
			public static uint IsAI = 0xA0; //_IsAI_k__BackingField
			public static uint NickName = 0xB8; //_NickName_k__BackingField
			public static uint AccountId = 0xC0; //_AccountId_k__BackingField
			public static uint PlayerBody = 0xD8; //_PlayerBody_k__BackingField
			public static uint Id = 0x7C; //_Id_k__BackingField
			public static uint VoipId = 0xB0; //_VoipID_k__BackingField
		}
		//public static class ObservedPlayerController
		public readonly partial struct ObservedPlayerController
		{
			public static uint InventoryController = 0x10; //_InventoryController_k__BackingField
			public static uint Player = 0x18; //_PlayerView_k__BackingField
			public static uint InfoContainer = 0xD0; //_InfoContainer_k__BackingField
			public static readonly uint[] MovementController = [0xD8, 0x98]; //_MovementController_k__BackingField
			public static uint HealthController = 0xE8; //_HealthController_k__BackingField
			public static uint HandsController = 0x120; //_HandsController_k__BackingField
		}
		//public static class ObservedPlayerStateContext
		public readonly partial struct ObservedMovementController
		{
			public static uint Rotation = 0x20; //_Rotation_k__BackingField
			public static uint Velocity = 0xF0; //_velocity
		}
		//public static class ObservedPlayerHandsController
		public readonly partial struct ObservedHandsController
		{
			public static uint ItemInHands = 0x58; //_item
			public static uint BundleAnimationBones = 0xA8; //_bundleAnimationBones
		}
		//public static class BundleAnimationBones
		public readonly partial struct BundleAnimationBonesController
		{
			public static uint ProceduralWeaponAnimationObs = 0xD0; //_ProceduralWeaponAnimation_k__BackingField
		}
		//public static class ProceduralWeaponAnimation
		public readonly partial struct ProceduralWeaponAnimationObs
		{
			public static uint _isAimingObs = 0x145; //_isAiming
		}
		//public static class ObservedPlayerHealthController
		public readonly partial struct ObservedHealthController
		{
			public static uint Player = 0x18; //_player
			public static uint PlayerCorpse = 0x20; //_playerCorpse
			public static uint HealthStatus = 0x10;
		}
		//public static class ProceduralWeaponAnimation
        public readonly partial struct ProceduralWeaponAnimation
        {
            public static uint ShotNeedsFovAdjustments = 0x433; //_ShotNeedsFovAdjustments_k__BackingField
            public static uint Breath = 0x38;
            public static uint PositionZeroSum = 0x31C;
            public static uint Shootingg = 0x58;
            public static uint _aimingSpeed = 0x164;
            public static uint _isAiming = 0x145;
            public static uint _optics = 0x180;
            public static uint _shotDirection = 0x1c8;
            public static uint Mask = 0x30;
            public static uint HandsContainer = 0x20;
            public static uint _fovCompensatoryDistance = 0x194;
			      		
        }
		//public static class PlayerSpring
		public readonly partial struct HandsContainer
		{
			public static uint CameraOffset = 0xDC;
            public static uint HandsRotation = 0x40;
            public static uint CameraRotation = 0x48;
            public static uint CameraPosition = 0x50;
		}
		//public static class SightNBone
		public readonly partial struct SightNBone
		{
			public static uint Mod = 0x10;
		}
		//public static class ShotEffector
		public readonly partial struct ShotEffector
		{
			public static uint NewShotRecoil = 0x20;
		}
		//public static class PlayerStateContainer
        public readonly partial struct PlayerStateContainer
        {
            public static uint Name = 0x19;
            public static uint StateFullNameHash = 0x40;
        }
		//public static class NewRecoilShotEffect
		public readonly partial struct NewShotRecoil
		{
			public static uint IntensitySeparateFactors = 0x94;
		}
		//public static class VisorEffect
		public readonly partial struct VisorEffect
		{
			public static uint Intensity = 0x20;
		}
		//public static class TOD_Time
		public readonly partial struct TOD_Time
		{
			public static uint LockCurrentTime = 0x20;
		}
		//public static class TOD_CycleParameters
		public readonly partial struct TOD_CycleParameters
		{
			public static uint Hour = 0x10;
		}
		//public static class TOD_ImageEffect
		public readonly partial struct TOD_Scattering
		{
			public static uint Sky = 0x28;  //_sky
		}
		//public static class TOD_Sky
		public readonly partial struct TOD_Sky
		{
			public static uint Cycle = 0x38; //_Cycle_k__BackingField
			public static uint TOD_Components = 0xA0; //_Components_k__BackingField
		}
		//public static class TOD_Components
		public readonly partial struct TOD_Components
		{
			public static uint TOD_Time = 0x118; //_Time_k__BackingField
		}
		//public static class Profile
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
		//public static class WishlistManager
		public readonly partial struct WishlistManager
		{
			public static uint Items = 0x28; //_userItems
		}
		//public static class ProfileInfo
		public readonly partial struct PlayerInfo
		{
			public static uint Nickname = 0x10;
			public static uint EntryPoint = 0x28;
			public static uint Side = 0x48; //_Side_k__BackingField
            public static uint RegistrationDate = 0x4C;
			public static uint GroupId = 0x50;
			public static uint Settings = 0x78; //_Settings_k__BackingField
            public static uint MemberCategory = 0x80;
			public static uint Experience = 0x84; //_experience
        }
		//public static class SkillManager
		public readonly partial struct SkillManager
		{
			public static uint StrengthBuffJumpHeightInc = 0x60;
			public static uint StrengthBuffThrowDistanceInc = 0x70;
			public static uint MagDrillsLoadSpeed = 0x180;
			public static uint MagDrillsUnloadSpeed = 0x188;
    		public static uint RaidLoadedAmmoAction   = 0x480;
    		public static uint RaidUnloadedAmmoAction = 0x488;
		}
		//public static class FloatBuff
		public readonly partial struct SkillValueContainer
		{
			public static uint Value = 0x30;
		}
		//public static class QuestStatusData
		public readonly partial struct QuestData
		{
			public static uint Id = 0x10;
			public static uint Status = 0x1C;
			public static uint CompletedConditions = 0x28;
			public static uint Template = 0x38;
		}
		//public static class CompletedConditionsCollection
		public readonly partial struct CompletedConditionsCollection
		{
			public static uint BackendData = 0x10; //_backendData
			public static uint LocalChanges = 0x18; //_localChanges
		}
		//public static class QuestTemplate
		public readonly partial struct QuestTemplate
		{
			public static uint Conditions = 0x60; //_Conditions_k__BackingField
			public static uint Name = 0xC8; //_questName
		}
		//?
		public readonly partial struct QuestConditionsContainer
		{
			public static uint ConditionsList = 0x70;
		}
		//public static class ItemHandsController
		public readonly partial struct ItemHandsController
		{
			public static uint Item = 0x70; //_item
		}
		//public static class FirearmController
		public readonly partial struct FirearmController
		{
			public static uint Fireport = 0x150;
			public static uint TotalCenterOfImpact = 0xF0; //COI
			public static uint WeaponLn = 0x100;
		}
		//public static class ClientFirearmController + public static class FirearmController
		public readonly partial struct ClientFirearmController
		{
			public static uint WeaponLn = 0x100; //from FirearmController
			public static uint ShotIndex = 0x438; //LastShotId
        }
		//public static class MovementContext
        public readonly partial struct MovementContext
        {
            public static uint Player = 0x48; //_player
            public static uint _rotation = 0xC8;
            public static uint PlantState = 0x78;
            public static uint CurrentState = 0x1F0; //_CurrentState_k__BackingField
            public static uint _states = 0x480;
            public static uint _movementStates = 0x4B0;
            public static uint _tilt = 0xB4;
            public static uint _physicalCondition = 0x198;
            public static uint _speedLimitIsDirty = 0x1B9;
            public static uint StateSpeedLimit = 0x1BC; //_StateSpeedLimit_k__BackingField
            public static uint StateSprintSpeedLimit = 0x1C0; //_StateSprintSpeedLimit_k__BackingField
            public static uint _lookDirection = 0x3B8;
            public static uint WalkInertia = 0x4bC; //_WalkInertia_k__BackingField
            public static uint SprintBrakeInertia = 0x4C0; //_SprintBrakeInertia_k__BackingField
            public static uint _poseInertia = 0x4C4;
			public static uint _currentPoseInertia = 0x4C8;
			public static uint _inertiaAppliedTime = 0x26C;			
        }
		//public static class MovementState + public static class BaseMovementState + public static class MovePlayerState
        public readonly partial struct MovementState
        {
            public static uint StickToGround = 0x54;//from MovementState
            public static uint PlantTime = 0x58; //from MovementState
            public static uint Name = 0x11; //from BaseMovementState
            public static uint AnimatorStateHash = 0x20; //from BaseMovementState
            public static uint _velocity = 0xDC; //from MovePlayerState
            public static uint _velocity2 = 0xF0; //from MovePlayerState
			public static uint AuthoritySpeed = 0x28; //from BaseMovementState
        }
		//public static class InventoryController
		public readonly partial struct InventoryController
		{
			public static uint Inventory = 0x100; //_Inventory_k__BackingField
		}
		//public static class Inventory
		public readonly partial struct Inventory
		{
			public static uint Equipment = 0x18;
			public static uint QuestRaidItems = 0x20;
			public static uint QuestStashItems = 0x28;
			public static uint Stash = 0x20;
		}
		//public static class Stash + ? might be using public static class CompoundItem
		public readonly partial struct Stash
		{
			public static uint Grids = 0x98; //_grid
            public static uint Slots = 0x80; //from CompoundItem
		}
		//public static class CompoundItem
		public readonly partial struct Equipment
		{
			public static uint Grids = 0x78;
			public static uint Slots = 0x80;
		}
		//public static class BarterOther
		public readonly partial struct BarterOtherOffsets
		{
			public static uint Dogtag = 0x80;
		}
		//public static class DogtagComponent
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
		//public static class Grid
		public readonly partial struct Grids
		{
			public static uint ContainedItems = 0x48; //_ItemCollection_k__BackingField
		}
		//public static class GridItemCollection
		public readonly partial struct GridContainedItems
		{
			public static uint Items = 0x18; //ItemsList
		}
		//public static class Slot
		public readonly partial struct Slot
		{
			public static uint ContainedItem = 0x48; //_ContainedItem_k__BackingField
			public static uint ID = 0x58; //_ID_k__BackingField
			public static uint Required = 0x18;
		}
		//public static class LootItem
		public readonly partial struct InteractiveLootItem
		{
			public static uint Item = 0xF0; //_item
		}
		//public static class Skeleton
		public readonly partial struct DizSkinningSkeleton
		{
			public static uint _values = 0x30;
		}
		//public static class LootableContainer + public static class WorldInteractiveObject
		public readonly partial struct LootableContainer
		{
			public static uint InteractingPlayer = 0x150; //from WorldInteractiveObject _InteractingPlayer_k__BackingField
			public static uint ItemOwner = 0x168;
			public static uint Template = 0x170;
		}
		//public static class ItemController
		public readonly partial struct LootableContainerItemOwner
		{
			public static uint RootItem = 0xD0; //_RootItem_k__BackingField
		}
		//public static class Item
		public readonly partial struct LootItem
		{
			public static uint StackObjectsCount = 0x24;
			public static uint Version = 0x28;
    		public static uint Components = 0x40;
			public static uint Template = 0x60; //_Template_k__BackingField
            public static uint SpawnedInSession = 0x68; //_SpawnedInSession_k__BackingField
        }
		//public static class CompoundItem
		public readonly partial struct LootItemMod
		{
			public static uint Grids = 0x78;
			public static uint Slots = 0x80;
		}
		//public static class Grid
		public readonly partial struct Grid
		{
			public static uint ItemCollection = 0x48; //_ItemCollection_k__BackingField
		}
		//public static class GridItemCollection
		public readonly partial struct GridItemCollection
		{
		    public static uint ItemsList = 0x18;
		}
		//public static class Weapon
		public readonly partial struct LootItemWeapon
		{
			public static uint FireMode = 0xA0;
			public static uint Chambers = 0xB0; //_Chambers_k__BackingField
			public static uint _magSlotCache = 0xC8;
		}
		//public static class LevelSettings
        public readonly partial struct LevelSettings
        {
            public static uint AmbientMode = 0x60;
            public static uint EquatorColor = 0x74;
            public static uint GroundColor = 0x84;
        }
		//public static class SlotView_2
		public readonly partial struct PlayerBodySubclass
		{
			public static uint Dresses = 0x40;
		}
		//public static class Dress
		public readonly partial struct Dress
		{
			public static uint Renderers = 0x38;
		}
		//?
		public readonly partial struct SlotViewsContainer
		{
			public static uint Dict = 0x10;
		}
		//public static class EFTHardSettings
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
		//public static class EftScreenManager
		public readonly partial struct EftScreenManager
		{
			public static uint _instance = 0x0;
		}
		//public static class GPUInstancerManager
		public readonly partial struct GPUInstancerManager
		{
			public static uint Instance = 0x0;
			public static uint runtimeDataList = 0x58;
		}
		//public static class ClientBackendSession
		public readonly partial struct ClientBackendSession
		{
			public static uint BackEndConfig = 0x158; //_BackEndConfig_k__BackingField
		}
		//?
		public readonly partial struct BSGGameSettingValueClass
		{
			public static uint Value = 0x30;
		}
		//?
		public readonly partial struct BSGGameSetting
		{
			public static uint ValueClass = 0x28;
		}
		//public static class FireModeComponent
		public readonly partial struct FireModeComponent
		{
			public static uint FireMode = 0x28;
		}
		//public static class MagazineTemplate
		public readonly partial struct LootItemMagazine
		{
			public static uint Cartridges = 0xA8;
			public static uint LoadUnloadModifier = 0x1B0;
		}
		//public static class Item
		public readonly partial struct MagazineClass
		{
			public static uint StackObjectsCount = 0x24;
		}
		//public static class StackSlot
		public readonly partial struct StackSlot
		{
			public static uint _items = 0x18;
			public static uint MaxCount = 0x10;
		}
		//public static class ItemTemplate
		public readonly partial struct ItemTemplate
		{
			public static uint Name = 0x10;
			public static uint ShortName = 0x18;
			public static uint _id = 0xE0; //__id_k__BackingField
            public static uint Weight = 0xB0;
			public static uint QuestItem = 0x34;
		}
		//public static class ModTemplate
		public readonly partial struct ModTemplate
		{
			public static uint Velocity = 0x188;
		}
		//public static class AmmoTemplate
		public readonly partial struct AmmoTemplate
		{
			public static uint InitialSpeed = 0x1A4;
			public static uint BallisticCoeficient = 0x1B8;
			public static uint BulletMassGram = 0x25C;
			public static uint BulletDiameterMilimeters = 0x260;
		}
		//public static class WeaponTemplate
		public readonly partial struct WeaponTemplate
		{
			public static uint Velocity = 0x254;
            public static uint AllowJam = 0x308;
            public static uint AllowFeed = 0x309;
            public static uint AllowMisfire = 0x30A;
            public static uint AllowSlide = 0x30B;			
		}
		//public static class PlayerBody
		public readonly partial struct PlayerBody
		{
			public static uint SkeletonRootJoint = 0x30;
			public static uint BodySkins = 0x58;
			public static uint _bodyRenderers = 0x68;
			public static uint SlotViews = 0x90;
			public static uint PointOfView = 0xC0;
		}
		//public static class InventoryBlur
        public readonly partial struct InventoryBlur
        {
            public static uint _blurCount = 0x38;
            public static uint _upsampleTexDimension = 0x30;
        }
        // public static class PhysicalBase
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
			//public static uint SprintWeightFactor = 0x104;
            public static uint PreviousWeight = 0xD4;
            public static uint SprintAcceleration = 0x114;
            public static uint PreSprintAcceleration = 0x118;
			public static uint _encumbered = 0x11C;
            public static uint _overEncumbered = 0x11D;
			public static uint SprintOverweight = 0xD0;
			public static uint BerserkRestorationFactor = 0x110; //_BerserkRestorationFactor_k__BackingField
        }
		//public static class Stamina
        public readonly partial struct PhysicalValue //Class: .Stamina
        {
            public static uint Current = 0x10;
        }
		//public static class BreathEffector
        public readonly partial struct BreathEffector //Class: EFT.Animations.BreathEffector
        {
            public static uint Intensity = 0x30;
        }
		//public static class OpticCameraManager
		public readonly partial struct OpticCameraManager
		{
			public static uint Camera = 0x70; //_Camera_k__BackingField
            public static uint CurrentOpticSight = 0x78; //_CurrentOpticSight_k__BackingField
        }
		//public static class GPUInstancerRuntimeData
		public readonly partial struct GPUInstancerRuntimeData
        {
            public static uint instanceBounds = 0x20;
        }
		//public static class CameraManager
		public readonly partial struct EFTCameraManager
		{
			public static uint OpticCameraManager = 0x10; //_OpticCameraManager_k__BackingField
			public static uint Camera = 0x60; //_Camera_k__BackingField
			public static uint GetInstance_RVA = 0x2CF8AB0; //get_Instance_RVA
		}
		//public static class SightComponent
		public readonly partial struct SightComponent
		{
			public const uint _template = 0x20;
			public const uint ScopesSelectedModes = 0x30;
			public const uint SelectedScope = 0x38;
			public const uint ScopeZoomValue = 0x3C;
		}
		//public static class SightModTemplate
		public readonly partial struct SightInterface
		{
			public const uint Zooms = 0x1B8;
		}
		//public static class WeatherController
		public readonly partial struct WeatherController
		{
			public static uint Instance = 0x0;
			public static uint WeatherDebug = 0x88;
		}
		//public static class WeatherDebug
        public readonly partial struct WeatherDebug
        {
            public static uint CloudDensity = 0x24;
            public static uint Fog = 0x28;
            public static uint LightningThunderProbability = 0x30;
            public static uint Rain = 0x2c;
            public static uint WindMagnitude = 0x14;
            public static uint isEnabled = 0x10;
        }
		//
		public readonly partial struct Special
		{
			public static ulong TypeInfoTableRva = 0x5AA9118;
			public static uint EFTHardSettings_TypeIndex = 225;
			public static uint GPUInstancerManager_TypeIndex = 4917;
			public static uint WeatherController_TypeIndex = 10104;
			public static uint GlobalConfiguration_TypeIndex = 6406;
			public static uint MatchingProgress_TypeIndex = 15331;
			public static uint MatchingProgressView_TypeIndex = 15334;
		}
		// MatchingProgress — pure data model, instance fields
		public readonly partial struct MatchingProgress
		{
			public const uint StatusUpdateEvent                  = 0x10;
			public const uint MatchingProgressChangedEvent       = 0x18;
			public const uint CurrentStage                       = 0x20; // int / enum
			public const uint CurrentStageGroup                  = 0x24; // int / enum
			public const uint CurrentStageProgress               = 0x28; // float
			public const uint EstimateTime                       = 0x30; // int
			public const uint StartTime                          = 0x38; // DateTime (8 bytes)
			public const uint IsAbortAvailable                   = 0x40; // bool
			public const uint BlockAbortAbilityDurationSeconds   = 0x44; // int
			public const uint ShowAbortConfirmationPopup         = 0x48; // bool
			public const uint IsMatchingAbortRequested           = 0x49; // bool
			public const uint LastMemorizedDelayedStage          = 0x4C;
			public const uint LastMemorizedDelayedStageProgress  = 0x54;
			public const uint CanProcessServerStages             = 0x5C; // bool
		}
		// MatchingProgressView — MonoBehaviour that owns the MatchingProgress instance
		public readonly partial struct MatchingProgressView
		{
			public const uint _matchingProgress             = 0x130;
			public const uint _lastUpdateTime               = 0x138; // DateTime (8-byte ticks)
			public const uint _matchingWarningType          = 0x148; // EMatchingWarning? (nullable int at +0x148, hasValue bool at +0x14C)
			public const uint _matchingWarningType_hasValue = 0x14C; // bool — nullable backing hasValue
			public const uint _serversLimited               = 0x160; // bool
			public const uint _canUpdateStatus              = 0x161; // bool
			public const uint _maxMatchingTimeInSeconds     = 0x164; // int
		}
		public readonly partial struct Il2CppClass
		{
			public const uint Name         = 0x10;
			public const uint Namespace    = 0x18;
			public const uint Fields       = 0x80;
			public const uint StaticFields = 0xB8;
			public const uint Methods      = 0x98;
			public const uint MethodCount  = 0x120;
			public const uint FieldCount   = 0x124;
		}
	}

	public readonly partial struct Enums
	{
		public enum EPlayerState
		{
			None = 0,
			Idle = 1,
			ProneIdle = 2,
			ProneMove = 3,
			Run = 4,
			Sprint = 5,
			Jump = 6,
			FallDown = 7,
			Transition = 8,
			BreachDoor = 9,
			Loot = 10,
			Pickup = 11,
			Open = 12,
			Close = 13,
			Unlock = 14,
			Sidestep = 15,
			DoorInteraction = 16,
			Approach = 17,
			Prone2Stand = 18,
			Transit2Prone = 19,
			Plant = 20,
			Stationary = 21,
			Roll = 22,
			JumpLanding = 23,
			ClimbOver = 24,
			ClimbUp = 25,
			VaultingFallDown = 26,
			VaultingLanding = 27,
			BlindFire = 28,
			IdleWeaponMounting = 29,
			IdleZombieState = 30,
			MoveZombieState = 31,
			TurnZombieState = 32,
			StartMoveZombieState = 33,
			EndMoveZombieState = 34,
			DoorInteractionZombieState = 35,
		}

		// TODO: dump EMatchingWarning enum values from IL2CPP — field offsets are 0x0 in static region, int values unknown
		public enum EMatchingWarning
		{
			// values need confirming from IL2CPP dump / dnSpy
		}

		public enum EMatchingStage
		{
			None                          = 0,
			GameWorldCreating             = 1,
			BundlesLoading                = 2,
			PoolsCreating                 = 3,
			MapLoading                    = 4,
			DataCaching                   = 5,
			PlayersSearching              = 6,
			ServerSearching               = 7,
			ServerStartAwaiting           = 8,
			GamePreparing                 = 9,
			ServerConnecting              = 10,
			ServerResponseAwaiting        = 11,
			LootBundlesLoading            = 12,
			LootPoolsCreating             = 13,
			SessionStartAwaiting          = 14,
			LocalGameStarting             = 15,
			PlayersAwaiting               = 16,
			SynchronizationWithOtherPlayers = 17,
			GameLeaving                   = 18,
		}

		public enum EMatchingStageGroup
		{
			GameWorldCreating   = 0,
			MapLoading          = 1,
			PlayersSearching    = 2,
			ServerSearching     = 3,
			ServerStartAwaiting = 4,
			ServerConnecting    = 5,
			LootLoading         = 6,
			SessionStartAwaiting = 7,
			PlayersAwaiting     = 8,
		}

		[Flags]
		public enum EMemberCategory
		{
			Default = 0,
			Developer = 1,
			UniqueId = 2,
			Trader = 4,
			Group = 8,
			System = 16,
			ChatModerator = 32,
			ChatModeratorWithPermanentBan = 64,
			UnitTest = 128,
			Sherpa = 256,
			Emissary = 512,
			Unheard = 1024,
		}

		public enum WildSpawnType
		{
			marksman = 0,
			assault = 1,
			bossTest = 2,
			bossBully = 3,
			followerTest = 4,
			followerBully = 5,
			bossKilla = 6,
			bossKojaniy = 7,
			followerKojaniy = 8,
			pmcBot = 9,
			cursedAssault = 10,
			bossGluhar = 11,
			followerGluharAssault = 12,
			followerGluharSecurity = 13,
			followerGluharScout = 14,
			followerGluharSnipe = 15,
			followerSanitar = 16,
			bossSanitar = 17,
			test = 18,
			assaultGroup = 19,
			sectantWarrior = 20,
			sectantPriest = 21,
			bossTagilla = 22,
			followerTagilla = 23,
			exUsec = 24,
			gifter = 25,
			bossKnight = 26,
			followerBigPipe = 27,
			followerBirdEye = 28,
			bossZryachiy = 29,
			followerZryachiy = 30,
			bossBoar = 32,
			followerBoar = 33,
			arenaFighter = 34,
			arenaFighterEvent = 35,
			bossBoarSniper = 36,
			crazyAssaultEvent = 37,
			peacefullZryachiyEvent = 38,
			sectactPriestEvent = 39,
			ravangeZryachiyEvent = 40,
			followerBoarClose1 = 41,
			followerBoarClose2 = 42,
			bossKolontay = 43,
			followerKolontayAssault = 44,
			followerKolontaySecurity = 45,
			shooterBTR = 46,
			bossPartisan = 47,
			spiritWinter = 48,
			spiritSpring = 49,
			peacemaker = 50,
			pmcBEAR = 51,
			pmcUSEC = 52,
			skier = 53,
			sectantPredvestnik = 57,
			sectantPrizrak = 58,
			sectantOni = 59,
			infectedAssault = 60,
			infectedPmc = 61,
			infectedCivil = 62,
			infectedLaborant = 63,
			infectedTagilla = 64,
			bossTagillaAgro = 65,
			bossKillaAgro = 66,
			tagillaHelperAgro = 67,
		}

		public enum EExfiltrationStatus
		{
			NotPresent = 1,
			UncompleteRequirements = 2,
			Countdown = 3,
			RegularMode = 4,
			Pending = 5,
			AwaitsManualActivation = 6,
			Hidden = 7,
		}

		[Flags]
		public enum EProceduralAnimationMask
		{
			Breathing = 1,
			Walking = 2,
			MotionReaction = 4,
			ForceReaction = 8,
			Shooting = 16,
			DrawDown = 32,
			Aiming = 64,
			HandShake = 128,
		}

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

	}

	public static class EMatchingStageExtensions
	{
		public static string ToDisplayString(this Enums.EMatchingStage stage) => stage switch
		{
			Enums.EMatchingStage.GameWorldCreating              => "Creating Game World",
			Enums.EMatchingStage.BundlesLoading                 => "Loading Bundles",
			Enums.EMatchingStage.PoolsCreating                  => "Creating Pools",
			Enums.EMatchingStage.MapLoading                     => "Loading Map",
			Enums.EMatchingStage.DataCaching                    => "Caching Data",
			Enums.EMatchingStage.PlayersSearching               => "Searching for Players",
			Enums.EMatchingStage.ServerSearching                => "Searching for Server",
			Enums.EMatchingStage.ServerStartAwaiting            => "Awaiting Server Start",
			Enums.EMatchingStage.GamePreparing                  => "Preparing Game",
			Enums.EMatchingStage.ServerConnecting               => "Connecting to Server",
			Enums.EMatchingStage.ServerResponseAwaiting         => "Awaiting Server Response",
			Enums.EMatchingStage.LootBundlesLoading             => "Loading Loot Bundles",
			Enums.EMatchingStage.LootPoolsCreating              => "Creating Loot Pools",
			Enums.EMatchingStage.SessionStartAwaiting           => "Awaiting Session Start",
			Enums.EMatchingStage.LocalGameStarting              => "Starting Game",
			Enums.EMatchingStage.PlayersAwaiting                => "Awaiting Players",
			Enums.EMatchingStage.SynchronizationWithOtherPlayers => "Synchronizing Players",
			Enums.EMatchingStage.GameLeaving                    => "Leaving Game",
			_                                                   => "Waiting for Raid Start",
		};
	}
}