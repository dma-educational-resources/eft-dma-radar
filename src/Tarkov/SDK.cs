namespace SDK
{
	public readonly partial struct ClassNames
	{
		public readonly partial struct NetworkContainer
		{
			public const uint ClassName_ClassToken = 0x2000661; // MDToken
			public const string ClassName = @"\uE32D";
		}

		public readonly partial struct AmmoTemplate
		{
			public const uint ClassName_ClassToken = 0x2002ABD; // MDToken
			public const uint MethodName_MethodToken = 0x60105B5; // MDToken
			public const string ClassName = @"\uEF1A";
			public const string MethodName = @"get_LoadUnloadModifier";
		}

		public readonly partial struct OpticCameraManagerContainer
		{
			public const uint ClassName_ClassToken = 0x2002F60; // MDToken
			public const string ClassName = @"\uF124";
		}

		public readonly partial struct ScreenManager
		{
			public const uint ClassName_ClassToken = 0x200369B; // MDToken
			public const string ClassName = @"\uF1EF";
		}

		public readonly partial struct FirearmController
		{
			public const uint ClassName_ClassToken = 0x20018A1; // MDToken
			public const string ClassName = @"EFT.Player+FirearmController";
		}

		public readonly partial struct ProceduralWeaponAnimation
		{
			public const uint ClassName_ClassToken = 0x20025B1; // MDToken
			public const uint MethodName_MethodToken = 0x600EAFC; // MDToken
			public const string ClassName = @"EFT.Animations.ProceduralWeaponAnimation";
			public const string MethodName = @"get_ShotNeedsFovAdjustments";
		}
	}

	public readonly partial struct Offsets
	{
    	public static class AssemblyCSharp
    	{
    	    public const uint TypeStart = 0;
    	    public const uint TypeCount = 16336;
    	}		
		public readonly partial struct TarkovApplication
		{
			public const uint _menuOperation = 0x130; // -.\uEA0Fget
			public const uint ClientBackEnd = 0x30; // -.\uEA0F
			public const uint HideoutControllerAccess = 0x158; // -.\uEA0Fget
		}

		public readonly partial struct MainMenuShowOperation
		{
			public const uint _afkMonitor = 0x38; // -.\uEA07
			public const uint _preloaderUI = 0x60; // -.\uEA07
			public const uint _profile = 0x50; // -.\uEA07
		}	
		public readonly partial struct PreloaderUI
		{
			public const uint _sessionIdText = 0x118;
			public const uint _alphaVersionLabel = 0x20;
		}	

		

		public readonly partial struct AfkMonitor
		{
			public const uint Delay = 0x10; // Single
		}
		public readonly partial struct GameWorld
		{
			public const uint Location = 0x90; // String (UNKN) - Note: dump shows LocationId at 0xC0 in GameWorld, but parser searches for Location field
			public const uint GameDateTime = 0xD0;
			public const uint SynchronizableObjectLogicProcessor = 0x240; // <SynchronizableObjectLogicProcessor>k__BackingField
		}

		public readonly partial struct ClientLocalGameWorld
		{
			public const uint BtrController = 0x20; // BtrController (DEC 3)
			public const uint TransitController = 0x30; // TransitController (DEC 3)
			public const uint ExfilController = 0x50; // ExfiltrationController (DEC 3)
			public const uint ClientShellingController = 0xA0; // ArtilleryShellingControllerClient (DEC 3)
			public const uint LocationId = 0xC8; // String (DEC 3)
			public const uint LootList = 0x190; // List<IKillable> (DEC 3)
			public const uint RegisteredPlayers = 0x1B0; // List<EFT.IPlayer> (DEC 3)
			public const uint BorderZones = 0x1E8; // BorderZone[] (DEC 3)
			public const uint MainPlayer = 0x208; // EFT.Player (DEC 3)
			public const uint World = 0x210; // EFT.World (DEC 3)
			public const uint SynchronizableObjectLogicProcessor = 0x240; // SynchronizableObjectLogicProcessor (DEC 3)
			public const uint Grenades = 0x280; // DictionaryListHydra<Int32, Throwable> (DEC 3)
		}

		public readonly partial struct TransitController
		{
			public const uint TransitPoints = 0x18; // System.Collections.Generic.Dictionary<Int32, TransitPoint> (DEC 3) - pointsById field
		}

		public readonly partial struct ClientShellingController
		{
			public const uint ActiveClientProjectiles = 0x68; // System.Collections.Generic.Dictionary<Int32, ArtilleryProjectileClient> (DEC 3)
		}

		public readonly partial struct WorldController
		{
			public const uint Interactables = 0x30; // EFT.Interactive.WorldInteractiveObject[] (UNKN)
		}

		public readonly partial struct Interactable
		{
			public const uint KeyId = 0x60; // String (DEC 3)
			public const uint Id = 0x70; // String (DEC 3)
			public const uint _doorState = 0xD0; // EDoorState (DEC 3)
		}

		public readonly partial struct ArtilleryProjectileClient
		{
			public const uint Position = 0x30; // UnityEngine.Vector3 (DEC 3) - _targetPosition field
			public const uint IsActive = 0x3C; // Boolean (DEC 3) - _flyOn field
		}

		public readonly partial struct TransitPoint
		{
			public const uint parameters = 0x20; // -.\uE6CC.Location.TransitParameters (DEC 3)
		}

		public readonly partial struct TransitParameters
		{
			public const uint id = 0x10; // int (DEC 3)
			public const uint active = 0x14; // bool (DEC 3)
			public const uint name = 0x18; // String (DEC 3)
			public const uint description = 0x20; // String (DEC 3)
			public const uint target = 0x38; // String (DEC 3)
			public const uint location = 0x40; // String (DEC 3) - FIXED from 0x30
		}

		public readonly partial struct SynchronizableObject
		{
			public const uint Type = 0x68; // SynchronizableObjectType (DEC 3)
		}

		public readonly partial struct SynchronizableObjectLogicProcessor
		{
			public const uint _activeSynchronizableObjects = 0x18; // _activeSynchronizableObjects
		}

		public readonly partial struct TripwireSynchronizableObject
		{
			public const uint GrenadeTemplateId = 0x130; // EFT.MongoID (DEC 3) - <GrenadeTemplateId>k__BackingField
			public const uint _tripwireState = 0xE4; // ETripwireState (DEC 3)
			public const uint FromPosition = 0x14C; // UnityEngine.Vector3 (DEC 3) - <FromPosition>k__BackingField
			public const uint ToPosition = 0x158; // UnityEngine.Vector3 (DEC 3) - <ToPosition>k__BackingField
		}

		public readonly partial struct MineDirectional
		{
			public const uint Mines = 0x8; // System.Collections.Generic.List<MineDirectional> (UNKN)
			public const uint MineData = 0x20; // -.MineDirectional.MineSettings (UNKN)
		}

		public readonly partial struct MineSettings
		{
			public const uint _maxExplosionDistance = 0x28; // Single (UNKN)
			public const uint _directionalDamageAngle = 0x64; // Single (UNKN)
		}

		public readonly partial struct BorderZone
		{
			public const uint Description = 0x60; // String (DEC 3) - <Description>k__BackingField
			public const uint _extents = 0x28; // UnityEngine.Vector3 (DEC 3)
		}

		public readonly partial struct BtrController
		{
			public const uint BtrView = 0x50; // EFT.Vehicle.BTRView (DEC 3) - <BtrView>k__BackingField
		}

		public readonly partial struct BTRView
		{
			public const uint turret = 0x60; // EFT.Vehicle.BTRTurretView (DEC 3)
			public const uint _targetPosition = 0xAC; // UnityEngine.Vector3 (DEC 3)
			public const uint _previousPosition = 0xB4; // UnityEngine.Vector3 (DEC 3)
		}

		public readonly partial struct BTRTurretView
		{
			public const uint AttachedBot = 0x60; // System.ValueTuple<ObservedPlayerView, Boolean> (DEC 3) - _bot field
		}

		public readonly partial struct QuestConditionZone
		{
			public const uint target = 0x98; // System.String[] (IL2CPP - was 0x70 in Mono)
			public const uint zoneId = 0xA0; // String (DEC 1)
		}

		public readonly partial struct QuestConditionLaunchFlare
		{
			public const uint zoneId = 0x98; // String (IL2CPP - was 0x70 in Mono)
		}
        public static class EffectsController
        {
            public const uint DEFAULT_FOCAL_LENGTH = 0x0; // DEFAULT_FOCAL_LENGTH
            public const uint MED_KIT_EFFECT_DURATION = 0x0; // MED_KIT_EFFECT_DURATION
            public const uint AIM_PUNCH_COEFFICIENT = 0x0; // AIM_PUNCH_COEFFICIENT
            public const uint _effectsPrefab = 0x20; // _effectsPrefab
            public const uint FastVineteFlicker = 0x28; // FastVineteFlicker
            public const uint RainScreenDrops = 0x30; // <RainScreenDrops>k__BackingField
            public const uint ScreenWater = 0x38; // <ScreenWater>k__BackingField
            public const uint _vignette = 0x40; // _vignette
            public const uint _doubleVision = 0x48; // _doubleVision
            public const uint _hueFocus = 0x50; // _hueFocus
            public const uint _radialBlur = 0x58; // _radialBlur
            public const uint _sharpen = 0x60; // _sharpen
            public const uint _lowhHealthBlend = 0x68; // _lowhHealthBlend
            public const uint _bloodlossBlend = 0x70; // _bloodlossBlend
            public const uint _wiggle = 0x78; // _wiggle
            public const uint _motionBluer = 0x80; // _motionBluer
            public const uint _bloodOnScreen = 0x88; // _bloodOnScreen
            public const uint _grenadeFlash = 0x90; // _grenadeFlash
            public const uint _eyeBurn = 0x98; // _eyeBurn
            public const uint _blur = 0xA0; // _blur
            public const uint _dof = 0xA8; // _dof
            public const uint _effectAccumulators = 0xB0; // _effectAccumulators
            public const uint _sharpenAccumulator = 0xB8; // _sharpenAccumulator
            public const uint _radialBlurAccumulator = 0xC0; // _radialBlurAccumulator
            public const uint _chromaticAberration = 0xC8; // _chromaticAberration
            public const uint _thermalVision = 0xD0; // _thermalVision
            public const uint _frostbiteEffect = 0xD8; // _frostbiteEffect	
		}	
        public static class FrostbiteEffect
        {
            public const uint SHADER_NAME = 0x0; // SHADER_NAME
            public const uint _baseColorId = 0x0; // _baseColorId
            public const uint _baseColorMapId = 0x4; // _baseColorMapId
            public const uint _normalMapId = 0x8; // _normalMapId
            public const uint _tilingId = 0xC; // _tilingId
            public const uint _ODRFId = 0x10; // _ODRFId
            public const uint _shapeRadiusId = 0x14; // _shapeRadiusId
            public const uint _ssaaPropagator = 0x20; // _ssaaPropagator
            public const uint _material = 0x28; // _material
            public const uint _shader = 0x30; // _shader
            public const uint _baseColor = 0x38; // _baseColor
            public const uint _baseColorMap = 0x48; // _baseColorMap
            public const uint _normalMap = 0x50; // _normalMap
            public const uint _tiling = 0x58; // _tiling
            public const uint _speed = 0x60; // _speed
            public const uint _opacity = 0x64; // _opacity
            public const uint _distortion = 0x68; // _distortion
            public const uint _radius = 0x6C; // _radius
            public const uint _shapeRadius = 0x70; // _shapeRadius
            public const uint _falloff = 0x78; // _falloff
        }
		public readonly partial struct QuestConditionInZone
		{
			public const uint zoneIds = 0x98; // System.String[] (IL2CPP - was 0x70 in Mono)
		}
		public readonly partial struct EftClientBackendSession
		{
			public const uint GetGlobalConfig_RVA = 0x436580;
		}
        public readonly partial struct GlobalConfiguration
        {
			public const uint Inertia = 0x1A8; // Inertia
		}	
		public readonly partial struct NightVision
		{
			public const uint _on = 0xC4; // Boolean (DEC 1)
		}

		public readonly partial struct ThermalVision
		{
			public const uint Material = 0xB8; // UnityEngine.Material (DEC 1)
			public const uint On = 0x20; // Boolean (DEC 1)
			public const uint IsNoisy = 0x21; // Boolean (DEC 1)
			public const uint IsFpsStuck = 0x22; // Boolean (DEC 1)
			public const uint IsMotionBlurred = 0x23; // Boolean (DEC 1)
			public const uint IsGlitch = 0x24; // Boolean (DEC 1)
			public const uint IsPixelated = 0x25; // Boolean (DEC 1)
			public const uint ChromaticAberrationThermalShift = 0x68; // Single (DEC 1)
			public const uint UnsharpRadiusBlur = 0x90; // Single (DEC 1)
			public const uint UnsharpBias = 0x94; // Single (DEC 1)
		}

		/// <summary>
		/// EFT.HealthSystem.ActiveHealthController/BaseHealthController - For LocalPlayer's health (IL2CPP)
		/// </summary>
		public readonly partial struct HealthController
		{
			public const uint Energy = 0x68; // IL2CPP DEC 2025 (from Camera-PWA)
			public const uint Hydration = 0x70; // IL2CPP DEC 2025 (from Camera-PWA)
		}

		public readonly partial struct ExfilController
		{
			public const uint ExfiltrationPointArray = 0x20; // ExfiltrationPoint[] (DEC 3) - <ExfiltrationPoints>k__BackingField
			public const uint ScavExfiltrationPointArray = 0x28; // ScavExfiltrationPoint[] (DEC 3) - <ScavExfiltrationPoints>k__BackingField
			public const uint SecretExfiltrationPointArray = 0x30; // SecretExfiltrationPoint[] (DEC 3) - <SecretExfiltrationPoints>k__BackingField
		}

		public readonly partial struct Exfil
		{
			public const uint _status = 0x58; // EExfiltrationStatus (DEC 1)
			public const uint Settings = 0x98; // ExitTriggerSettings (DEC 1)
			public const uint EligibleEntryPoints = 0xC0; // String[] (DEC 1)
		}

		public readonly partial struct ScavExfil
		{
			public const uint EligibleIds = 0xF8; // List<String> 
		}

		public readonly partial struct ExfilSettings
		{
			public const uint Name = 0x18; // String 
		}

		public readonly partial struct GenericCollectionContainer
		{
			public const uint List = 0x18; // System.Collections.Generic.List<Var> (UNKN)
		}

		public readonly partial struct Grenade
		{
			public const uint IsDestroyed = 0x4D; // Boolean (DEC 1)
			public const uint WeaponSource = 0x80; // -.\uEF81 (UNKN)
		}

		public readonly partial struct GamePlayerOwner
		{
			public const uint _myPlayer = 0x8; // EFT.Player (DEC 3)
		}
		public readonly partial struct Player
		{
			public const uint _characterController = 0x40; // ICharacterController (DEC 3)
			public const uint MovementContext = 0x60; // MovementContext (DEC 3)
			public const uint _playerBody = 0x190; // PlayerBody (DEC 3)
			public const uint ProceduralWeaponAnimation = 0x338; // ProceduralWeaponAnimation (DEC 3)
			public const uint _animators = 0x640; // IAnimator[] (DEC 3)
			public const uint EnabledAnimators = 0x670; // EAnimatorMask (DEC 3)
			public const uint Corpse = 0x680; // Corpse (DEC 3)
			public const uint Location = 0x870; // String (DEC 3)
			public const uint InteractableObject = 0x888; // InteractableObject (DEC 3)
			public const uint Profile = 0x900; // Profile (DEC 3)
			public const uint Physical = 0x918; // PhysicalBase (DEC 3)
			public const uint AIData = 0x940; // IAIData (DEC 3)
			public const uint _healthController = 0x960; // IHealthController (DEC 3)
			public const uint _inventoryController = 0x978; // PlayerInventoryController (DEC 3)
			public const uint _handsController = 0x980; // AbstractHandsController (DEC 3)
			public const uint InteractionRayOriginOnStartOperation = 0xA14; // UnityEngine.Vector3 (DEC 3)
			public const uint InteractionRayDirectionOnStartOperation = 0xA00; // UnityEngine.Vector3 (DEC 3)
			public const uint IsYourPlayer = 0xA81; // Boolean (DEC 3)
			public const uint VoipID = 0x8f0; // Boolean (DEC 3)
			public const uint Id = 0x8f8; 
			public const uint GameWorld = 0x5f8; // EFT.GameWorld (DEC 3)
		}

		public readonly partial struct ObservedPlayerView
		{
			public const uint ObservedPlayerController = 0x28; // ObservedPlayerController (DEC 3) - <ObservedPlayerController>k__BackingField
			public const uint Voice = 0x40; // String (DEC 3) - <Voice>k__BackingField
			public const uint VisibleToCameraType = 0x60; // ECameraType (DEC 3) - <VisibleToCameraType>k__BackingField
			public const uint GroupID = 0x80; // String (DEC 3) - <GroupId>k__BackingField
			public const uint Side = 0x94; // EPlayerSide (DEC 3) - <Side>k__BackingField
			public const uint IsAI = 0xa0; // Boolean (DEC 3) - <IsAI>k__BackingField
			public const uint NickName = 0xb8; // String (DEC 3) - <NickName>k__BackingField
			public const uint AccountId = 0xc0; // String (DEC 3) - <AccountId>k__BackingField
			public const uint PlayerBody = 0xd8; // PlayerBody (DEC 3) - <PlayerBody>k__BackingField
			public const uint Id = 0x7c; 
			public const uint VoipId = 0xB0; 
		}

		public readonly partial struct ObservedPlayerController
		{
			public const uint InventoryController = 0x10; // ObservedPlayerInventoryController (DEC 3) - <InventoryController>k__BackingField
			public const uint Player = 0x18; // ObservedPlayerView (DEC 3) - <PlayerView>k__BackingField
			public const uint InfoContainer = 0xD0; // ObservedPlayerInfoContainer (DEC 3) - <InfoContainer>k__BackingField
			public static readonly uint[] MovementController = new uint[] { 0xD8, 0x98 }; // ObservedPlayerMovementController, ObservedPlayerStateContext (DEC 3) - <MovementController>k__BackingField, then <ObservedPlayerStateContext>k__BackingField
			public const uint HealthController = 0xE8; // ObservedPlayerHealthController (DEC 3) - <HealthController>k__BackingField
			public const uint HandsController = 0x120; // ObservedPlayerHandsController (DEC 3) - <HandsController>k__BackingField
		}

		public readonly partial struct ObservedMovementController
		{
			public const uint Rotation = 0x20; // UnityEngine.Vector2 (DEC 3) - <Rotation>k__BackingField in ObservedPlayerStateContext
			public const uint Velocity = 0xF0; // UnityEngine.Vector3 (DEC 3) - _velocity field in ObservedPlayerStateContext
		}

		public readonly partial struct ObservedHandsController
		{
			public const uint ItemInHands = 0x58; // EFT.InventoryLogic.Item (DEC 3) - _item field
			public const uint BundleAnimationBones = 0xA8; // BundleAnimationBones (DEC 3) - _bundleAnimationBones field
		}

		public readonly partial struct BundleAnimationBonesController
		{
			public const uint ProceduralWeaponAnimationObs = 0xD0; // EFT.Animations.ProceduralWeaponAnimation (UNKN)
		}

		public readonly partial struct ProceduralWeaponAnimationObs
		{
			public const uint _isAimingObs = 0x145; // Boolean (UNKN)
		}

		public readonly partial struct ObservedHealthController
		{
			public const uint Player = 0x18; // EFT.NextObservedPlayer.ObservedPlayerView (DEC 3) - _player field
			public const uint PlayerCorpse = 0x20; // EFT.Interactive.ObservedCorpse (DEC 3) - _playerCorpse field
			public const uint HealthStatus = 0x10; // ETagStatus (DEC 3)
		}

		public readonly partial struct SimpleCharacterController
		{
			public const uint _collisionMask = 0x38; // UnityEngine.LayerMask (DEC 3)
			public const uint _speedLimit = 0x54; // Single (DEC 3)
			public const uint _sqrSpeedLimit = 0x58; // Single (DEC 3)
			public const uint velocity = 0xF0; // UnityEngine.Vector3 (DEC 3) - _velocity field
		}

		public readonly partial struct InfoContainer
		{
			public const uint Side = 0x18; // EPlayerSide (DEC 3) - <Side>k__BackingField
		}

		public readonly partial struct PlayerSpawnInfo
		{
			public const uint Side = 0x28; // System.Int32 (UNKN)
			public const uint WildSpawnType = 0x2C; // System.Int32 (UNKN)
		}


        public readonly partial struct ProceduralWeaponAnimation
        {
            public const uint ShotNeedsFovAdjustments = 0x433;
            public const uint Breath = 0x38;
            public const uint PositionZeroSum = 0x31c;
            public const uint Shootingg = 0x58;
            public const uint _aimingSpeed = 0x164;
            public const uint _isAiming = 0x145;
            public const uint _optics = 0x180;
            public const uint _shotDirection = 0x1c8;
            public const uint Mask = 0x30;
            public const uint HandsContainer = 0x20;
            public const uint _fovCompensatoryDistance = 0x194;
			      		
        }

		public readonly partial struct HandsContainer //PlayerSpring
		{
			public const uint CameraOffset = 0xDC; // UnityEngine.Vector3 (UNKN)
            public const uint HandsRotation = 0x40;
            public const uint CameraRotation = 0x48;
            public const uint CameraPosition = 0x50;
		}

		public readonly partial struct SightNBone
		{
			public const uint Mod = 0x10; // EFT.InventoryLogic.SightComponent (DEC 1)
		}

		public readonly partial struct MotionEffector
		{
			public const uint _mouseProcessors = 0x18; // -.\uE43A[] (UNKN)
			public const uint _movementProcessors = 0x20; // -.\uE439[] (UNKN)
		}

		public readonly partial struct ShotEffector
		{
			public const uint NewShotRecoil = 0x20; // EFT.Animations.NewRecoil.NewRecoilShotEffect (DEC 1)
		}

        public readonly partial struct PlayerStateContainer //Class: .PlayerStateContainer ---
        {
            public const uint Name = 0x19; // System.Byte
            public const uint StateFullNameHash = 0x40; // Int32 <StateFullNameHash> StateFullNameHash
        }

		public readonly partial struct NewShotRecoil
		{
			public const uint IntensitySeparateFactors = 0x94; // UnityEngine.Vector3 
		}

		public readonly partial struct VisorEffect
		{
			public const uint Intensity = 0x20; // Single (DEC 1)
		}

		// Time of Day (TOD) offsets for TimeOfDay feature
		public readonly partial struct TOD_Time
		{
			public const uint LockCurrentTime = 0x20; // Boolean (IL2CPP)
		}

		public readonly partial struct TOD_CycleParameters
		{
			public const uint Hour = 0x10; // Single (IL2CPP)
		}

		public readonly partial struct TOD_Scattering
		{
			public const uint Sky = 0x28; 
		}

		public readonly partial struct TOD_Sky
		{
			public const uint Cycle = 0x38; // -.TOD_CycleParameters
			public const uint TOD_Components = 0xA0; // -.TOD_Components
		}

		public readonly partial struct TOD_Components
		{
			public const uint TOD_Time = 0x118; // -.TOD_Time
		}

		// FullBright / CC_BrightnessContrastGamma offsets
		public readonly partial struct CC_BrightnessContrastGamma
		{
			public const uint Brightness = 0x38; // Single (IL2CPP)
			public const uint Contrast = 0x3C; // Single (IL2CPP)
			public const uint Gamma = 0x4C; // Single (IL2CPP)
		}

		public readonly partial struct Profile
		{
			public const uint Id = 0x10; // String (DEC 1)
			public const uint AccountId = 0x18; // String (DEC 1)
			public const uint Info = 0x48; // ProfileInfo (DEC 1)
			public const uint Inventory = 0x70; // SkillManager (DEC 1)
			public const uint Skills = 0x80; // SkillManager (DEC 1)
			public const uint TaskConditionCounters = 0x90; // Dictionary<MongoID, TaskConditionCounter> (DEC 1)
			public const uint QuestsData = 0x98; // List<QuestStatusData> (DEC 1)
			public const uint WishlistManager = 0x108; // WishlistManager (DEC 1)
			public const uint Stats = 0x148; // ProfileStatsSeparator (DEC 1)
		}

		public readonly partial struct WishlistManager
		{
			public const uint Items = 0x28; // System.Collections.Generic.Dictionary<MongoID, Int32> (UNKN)
		}

		public readonly partial struct PlayerInfo
		{
			public const uint Nickname = 0x10; // String (DEC 1)
			public const uint EntryPoint = 0x28; // String (DEC 1)
			public const uint Side = 0x48; // EPlayerSide (DEC 1)
			public const uint RegistrationDate = 0x4C; // Int32 (DEC 1)
			public const uint GroupId = 0x50; // String (DEC 1)
			public const uint Settings = 0x78; // ProfileSettings (DEC 1)
			public const uint MemberCategory = 0x80; // EMemberCategory (DEC 1)
			public const uint Experience = 0x84; // Int32 (DEC 1)
		}

		public readonly partial struct PlayerInfoSettings
		{
			public const uint Role = 0x10; // System.Int32 (UNKN)
		}

		public readonly partial struct SkillManager
		{
			public const uint StrengthBuffJumpHeightInc = 0x60; // -.SkillManager.\uE004 (DEC 1)
			public const uint StrengthBuffThrowDistanceInc = 0x70; // -.SkillManager.\uE004 (DEC 1)
			public const uint MagDrillsLoadSpeed = 0x180; // -.SkillManager.\uE004 (DEC 1)
			public const uint MagDrillsUnloadSpeed = 0x188; // -.SkillManager.\uE004 (DEC 1)
    		public const uint RaidLoadedAmmoAction   = 0x480;
    		public const uint RaidUnloadedAmmoAction = 0x488;
    		public const uint SpeedMultiplier = 0x30;
		}

		public readonly partial struct SkillValueContainer
		{
			public const uint Value = 0x30; // Single (UNKN)
		}

		public readonly partial struct QuestData
		{
			// QuestStatusData in IL2CPP dump
			public const uint Id = 0x10; // String
			public const uint Status = 0x1c; // EQuestStatus (IL2CPP - was 0x34 in Mono)
			public const uint CompletedConditions = 0x28; // CompletedConditionsCollection (IL2CPP - was 0x20 in Mono)
			public const uint Template = 0x38; // QuestTemplate (IL2CPP - was 0x28 in Mono)
		}
		
		/// <summary>
		/// CompletedConditionsCollection structure (IL2CPP)
		/// Contains backend and local HashSet&lt;MongoID&gt; for quest conditions
		/// </summary>
		public readonly partial struct CompletedConditionsCollection
		{
			public const uint BackendData = 0x10;   // HashSet<MongoID> - conditions from server
			public const uint LocalChanges = 0x18;  // HashSet<MongoID> - conditions completed this raid
		}

		public readonly partial struct QuestTemplate
		{
			// QuestTemplate in IL2CPP dump
			public const uint Conditions = 0x60; // ConditionsDict (IL2CPP - was 0x40 in Mono)
			public const uint Name = 0xC8; // String (_questName in dump)
		}

		public readonly partial struct QuestConditionsContainer
		{
			// ConditionCollection - the list is accessed via _necessaryConditions
			public const uint ConditionsList = 0x70; // IEnumerable<Condition> (IL2CPP - was 0x50 in Mono)
		}

		public readonly partial struct QuestCondition
		{
			public const uint id = 0x10; // EFT.MongoID (UNKN)
		}

		public readonly partial struct QuestConditionItem
		{
			public const uint value = 0x58; // Single (UNKN)
		}

		public readonly partial struct QuestConditionFindItem
		{
			public const uint target = 0x98; // System.String[] (IL2CPP - was 0x70 in Mono)
		}

		public readonly partial struct QuestConditionCounterCreator
		{
			public const uint Conditions = 0xa0; // ConditionCollection (IL2CPP - was 0x78 in Mono)
		}

		public readonly partial struct QuestConditionVisitPlace
		{
			public const uint target = 0x98; // String (IL2CPP - was 0x70 in Mono)
		}

		public readonly partial struct QuestConditionPlaceBeacon
		{
			public const uint zoneId = 0x98; // String (IL2CPP - inherited from ConditionOneTarget.target)
			public const uint plantTime = 0xa8; // Single (IL2CPP - was 0x80 in Mono)
		}

		public readonly partial struct QuestConditionCounterTemplate
		{
			public const uint Conditions = 0x10; // -.\uF267 (UNKN)
		}

		public readonly partial struct ItemHandsController
		{
			public const uint Item = 0x70; // EFT.InventoryLogic.Item (DEC 1)
		}

		public readonly partial struct FirearmController
		{
			public const uint Fireport = 0x150; // EFT.BifacialTransform (DEC 3)
			public const uint TotalCenterOfImpact = 0xF0; // Single (DEC 3) - COI field in dump
			public const uint WeaponLn = 0x100; // Single (DEC 3)
		}

		public readonly partial struct ClientFirearmController
		{
			public const uint WeaponLn = 0x100; // Single (DEC 3) - Inherited from FirearmController base
			public const uint ShotIndex = 0x438; // Byte (DEC 3) - LastShotId field in dump
		}

        public readonly partial struct MovementContext
        {
            public const uint Player = 0x48; // EFT.Player
            public const uint _rotation = 0xc8; // UnityEngine.Vector2
            public const uint PlantState = 0x78; // EFT.BaseMovementState <PlantState> PlantState
            public const uint CurrentState = 0x1F0; // EFT.BaseMovementState <CurrentState>k__BackingField
            public const uint _states = 0x480; // System.Collections.Generic.Dictionary<Byte, BaseMovementState> <_states> _states
            public const uint _movementStates = 0x4b0; // -.IPlayerStateContainerBehaviour[] <_movementStates> _movementStates
            public const uint _tilt = 0xb0; // Single <_tilt> _tilt
            public const uint _physicalCondition = 0x198; // System.Int32 <_physicalCondition> _physicalCondition
            public const uint _speedLimitIsDirty = 0x1b9; // Boolean <_speedLimitIsDirty> _speedLimitIsDirty
            public const uint StateSpeedLimit = 0x1bc; // Single <<StateSpeedLimit>k__BackingField> <StateSpeedLimit>k__BackingField
            public const uint StateSprintSpeedLimit = 0x1c0; // Single <<StateSprintSpeedLimit>k__BackingField> <StateSprintSpeedLimit>k__BackingField
            public const uint _lookDirection = 0x3b8; // UnityEngine.Vector3  <_lookDirection> _lookDirection
            public const uint WalkInertia = 0x4bc;
            public const uint SprintBrakeInertia = 0x4c0;
			public const uint _poseInertia = 0x4C4;
			public const uint _currentPoseInertia = 0x4C8;
			public const uint _inertiaAppliedTime = 0x26C;			
        }

        public readonly partial struct MovementState //Class: EFT.MovementState ---
        {
            public const uint StickToGround = 0x54; // Boolean <StickToGround> StickToGround
            public const uint PlantTime = 0x58; // Single <PlantTime> PlantTime
            public const uint Name = 0x11; // System.Byte <Name> Name
            public const uint AnimatorStateHash = 0x20; // Int32 <AnimatorStateName> AnimatorStateName
            public const uint _velocity = 0xDC;
            public const uint _velocity2 = 0xF0;	
			public const uint AuthoritySpeed = 0x28;		
        }

		public readonly partial struct InventoryController
		{
			public const uint Inventory = 0x100; // EFT.InventoryLogic.Inventory 
		}

		public readonly partial struct Inventory
		{
			public const uint Equipment = 0x18; // EFT.InventoryLogic.InventoryEquipment 
			public const uint QuestRaidItems = 0x20; // -.\uEFFE (UNKN)
			public const uint QuestStashItems = 0x28; // -.\uEFFE (UNKN)
			public const uint Stash = 0x20; // -.\uEFFE (UNKN)
		}

		public readonly partial struct Stash
		{
			public const uint Grids = 0x98; // -.\uEFFE (UNKN)
			public const uint Slots = 0x80; // -.\uEFFE (UNKN)
		}

		public readonly partial struct Equipment
		{
			public const uint Grids = 0x78; // -.\uEE74[] (UNKN)
			public const uint Slots = 0x80; // EFT.InventoryLogic.Slot[] (UNKN)
		}
		public readonly partial struct BarterOtherOffsets
		{
			public const uint Dogtag = 0x80; // EFT.InventoryLogic.BarterOther.Dogtag
		}
		public readonly partial struct DogtagComponent
		{
			public const uint Item = 0x10; // EFT.InventoryLogic.Item
			public const uint GroupId = 0x18; // string
			public const uint AccountId = 0x20; // string
			public const uint ProfileId = 0x28; // string
			public const uint Nickname = 0x30; // string
			public const uint Side = 0x38; // 
			public const uint Level = 0x3c; // int32_t
			public const uint Time = 0x40; // 
			public const uint Status = 0x48; // string
			public const uint KillerAccountId = 0x50; // string
			public const uint KillerProfileId = 0x58; // string
			public const uint KillerName = 0x60; // string
			 public const uint WeaponName = 0x68; // string
			 public const uint CarriedByGroupMember = 0x70; // bool
		}		
		public readonly partial struct Grids
		{
			public const uint ContainedItems = 0x48; // -.\uEE76 
		}

		public readonly partial struct GridContainedItems
		{
			public const uint Items = 0x18; // System.Collections.Generic.List<Item> 
		}

		public readonly partial struct Slot
		{
			public const uint ContainedItem = 0x48; // EFT.InventoryLogic.Item (DEC 1)
			public const uint ID = 0x58; // String (DEC 1)
			public const uint Required = 0x18; // Boolean (DEC 1)
		}

		public readonly partial struct InteractiveLootItem
		{
			public const uint Item = 0xF0; // EFT.InventoryLogic.Item (DEC 1)
		}

		public readonly partial struct InteractiveCorpse
		{
			public const uint PlayerBody = 0x130; // EFT.PlayerBody (UNKN)
		}

		public readonly partial struct DizSkinningSkeleton
		{
			public const uint _values = 0x30; // System.Collections.Generic.List<Transform> (DEC 1)
		}

		public readonly partial struct LootableContainer
		{
			public const uint InteractingPlayer = 0x150; // EFT.IPlayer (UNKN)
			public const uint ItemOwner = 0x168; // -.\uEFB4 (DEC 1)
			public const uint Template = 0x170; // String (UNKN)
		}

		public readonly partial struct LootableContainerItemOwner
		{
			public const uint RootItem = 0xD0; // EFT.InventoryLogic.Item 
		}

		public readonly partial struct LootItem
		{
			public const uint StackObjectsCount = 0x24; // Int32 
			public const uint Version = 0x28; // Int32 
    		public const uint Components = 0x40;
			public const uint Template = 0x60; // ItemTemplate 
			public const uint SpawnedInSession = 0x68; // Boolean (UNKN)
		}

		public readonly partial struct LootItemMod
		{
			public const uint Grids = 0x78; // -.\uEE74[] (UNKN)
			public const uint Slots = 0x80; // EFT.InventoryLogic.Slot[] (UNKN)
		}
		public readonly partial struct Grid
		{
			public const uint ItemCollection = 0x48; // EFT.InventoryLogic.Slot[] (UNKN)
		}
		public static class GridItemCollection
		{
		    public const uint ItemsList = 0x18; // List<EFT.InventoryLogic.Item>
		}		
		public readonly partial struct LootItemWeapon
		{
			public const uint FireMode = 0xA0; // EFT.InventoryLogic.FireModeComponent 
			public const uint Chambers = 0xB0; // EFT.InventoryLogic.Slot[] 
			public const uint _magSlotCache = 0xC8; // EFT.InventoryLogic.Slot 
		}

        public readonly partial struct LevelSettings
        {
            public const uint AmbientMode = 0x60;
            public const uint EquatorColor = 0x74;
            public const uint GroundColor = 0x84;
        }  
        public readonly partial struct InertiaSettings
        {
            public const uint BaseJumpPenalty = 0x54;
            public const uint BaseJumpPenaltyDuration = 0x4c;
            public const uint MoveTimeRange = 0xf4;
            public const uint FallThreshold = 0x20;
        }    
		public readonly partial struct PlayerBodySubclass
		{
			public const uint Dresses = 0x40; // EFT.Visual.Dress[]
		}

		public readonly partial struct Dress
		{
			public const uint Renderers = 0x38; // UnityEngine.Renderer[]
		}

		public readonly partial struct Skeleton
		{
			public const uint _values = 0x30; // System.Collections.Generic.List<Transform>
		}

		public readonly partial struct LoddedSkin
		{
			public const uint _lods = 0x20; // Diz.Skinning.AbstractSkin[]
		}

		public readonly partial struct Skin
		{
			public const uint _skinnedMeshRenderer = 0x28; // UnityEngine.SkinnedMeshRenderer
		}

		public readonly partial struct TorsoSkin
		{
			public const uint _skin = 0x28; // Diz.Skinning.Skin
		}

		public readonly partial struct SlotViewsContainer
		{
			public const uint Dict = 0x10; // System.Collections.Generic.Dictionary<Var, Var>
		}		    
        public readonly partial struct EFT
        {
            public readonly partial struct EFTHardSettings
            {
                public const uint POSE_CHANGING_SPEED = 0x380;
                public const uint _instance = 0x0;
                public const uint MED_EFFECT_USING_PANEL = 0x3b4;
                public const uint MOUSE_LOOK_HORIZONTAL_LIMIT = 0x340;
                public const uint MOUSE_LOOK_LIMIT_IN_AIMING_COEF = 0x350;
                public const uint MOUSE_LOOK_VERTICAL_LIMIT = 0x348;   
                public const uint ABOVE_OR_BELOW = 0x204;
                public const uint ABOVE_OR_BELOW_STAIRS = 0x20c;
                public const uint AIM_PROCEDURAL_INTENSITY = 0x3fc;
                public const uint AIR_CONTROL_BACK_DIR = 0x15c;
                public const uint AIR_CONTROL_NONE_OR_ORT_DIR = 0x160;
                public const uint AIR_CONTROL_SAME_DIR = 0x158;
                public const uint AIR_LERP = 0x3ac;
                public const uint AIR_MIN_SPEED = 0x3a8;   
                public const uint DecelerationSpeed = 0x50;   
            	public const uint WEAPON_OCCLUSION_LAYERS = 0x238; 
                public const uint DOOR_RAYCAST_DISTANCE = 0x18c;   
            	public const uint LOOT_RAYCAST_DISTANCE = 0x188;			                      
            }
            public static class EftScreenManager
            {
                // From your dumper:
                // Singleton Field : _instance (offset = 0x0)
                public const uint _instance = 0x0;
            }

            public static class WeatherController
            {
                // From your dumper:
                // Singleton Field : Instance (offset = 0x0)
                public const uint Instance = 0x0;
            }

            // GPUInstancerManager does NOT use a singleton ?? static fields only.
            public static class GPUInstancerManager
            {
                // No instance. But static fields size = 0xD0 from your dump.
                // We don't need any offsets here unless you want to read fields manually.
                public const uint StaticFieldsSize = 0xD0;
                public const uint Instance = 0x0;
            }	
			public static class ClientBackendSession
			{
				public const uint BackEndConfig = 0x158;
			}		
        }

		public readonly partial struct BSGGameSettingValueClass
		{
			public const uint Value = 0x30; // [HUMAN] T
		}		
		public readonly partial struct BSGGameSetting
		{
			public const uint ValueClass = 0x28; // [HUMAN] ulong
		}

		public readonly partial struct FireModeComponent
		{
			public const uint FireMode = 0x28; // System.Byte (UNKN)
		}

		public readonly partial struct LootItemMagazine
		{
			public const uint Cartridges = 0xA8; // EFT.InventoryLogic.Magazine.<Cartridges>k__BackingField 
			public const uint LoadUnloadModifier = 0x1B0; // Single 
		}

		public readonly partial struct MagazineClass
		{
			public const uint StackObjectsCount = 0x24; // EFT.InventoryLogic.Item.StackObjectsCount 
		}

		public readonly partial struct StackSlot
		{
			public const uint _items = 0x18; // System.Collections.Generic.List<Item> (DEC 1)
			public const uint MaxCount = 0x10; // Int32 (DEC 1)
		}

		public readonly partial struct ItemTemplate
		{
			public const uint Name = 0x10; // String (DEC 1)
			public const uint ShortName = 0x18; // String (DEC 1)
			public const uint _id = 0xE0; // EFT.MongoID (DEC 1)
			public const uint Weight = 0xB0; // Single (UNKN)
			public const uint QuestItem = 0x34; // Boolean (DEC 1)
		}

		public readonly partial struct MedicalTemplate
		{
			public const uint BodyPartTimeMults = 0x150; // System.Collections.Generic.KeyValuePair`2[] (DEC 1)
			public const uint HealthEffects = 0x158; // System.Collections.Generic.Dictionary<Int32, \uE6DC> (DEC 1)
			public const uint DamageEffects = 0x160; // System.Collections.Generic.Dictionary<Int32, \uE6DB> (DEC 1)
			public const uint StimulatorBuffs = 0x168; // String (DEC 1)
			public const uint UseTime = 0x148; // Single (DEC 1)
			public const uint MaxHpResource = 0x170; // Int32 (DEC 1)
			public const uint HpResourceRate = 0x174; // Single (DEC 1)
		}

		public readonly partial struct ModTemplate
		{
			public const uint Velocity = 0x188; // Single (DEC 1)
		}

		public readonly partial struct AmmoTemplate
		{
			public const uint InitialSpeed = 0x1A4; // Single (DEC 1)
			public const uint BallisticCoeficient = 0x1B8; // Single (DEC 1)
			public const uint BulletMassGram = 0x25C; // Single (DEC 1)
			public const uint BulletDiameterMilimeters = 0x260; // Single (DEC 1)
		}

		public readonly partial struct WeaponTemplate
		{
			public const uint Velocity = 0x254; // Single (DEC 1)
            public const uint AllowJam = 0x308;
            public const uint AllowFeed = 0x309;
            public const uint AllowMisfire = 0x30A;
            public const uint AllowSlide = 0x30B;			
		}

		public readonly partial struct PlayerBody
		{
			public const uint SkeletonRootJoint = 0x30; // Diz.Skinning.Skeleton (DEC 1)
			public const uint BodySkins = 0x58; // System.Collections.Generic.Dictionary<Int32, LoddedSkin> (UNKN)
			public const uint _bodyRenderers = 0x68; // -.\uE445[] (UNKN)
			public const uint SlotViews = 0x90; // -.\uE3D7<Int32, \uE001> (UNKN)
			public const uint PointOfView = 0xC0; // -.\uE772<Int32> (UNKN)
		}

		public readonly partial struct NetworkContainer
		{
			public const uint NextRequestIndex = 0x8; // Int64 (UNKN)
			public const uint PhpSessionId = 0x30; // String (UNKN)
			public const uint AppVersion = 0x38; // String (UNKN)
		}

		public readonly partial struct ScreenManager
		{
			public const uint Instance = 0x0; // -.\uF1EF (UNKN)
			public const uint CurrentScreenController = 0x28; // -.\uF1F1<Var> (UNKN)
		}
        public readonly partial struct InventoryBlur
        {
            public const uint _blurCount = 0x38;
            public const uint _upsampleTexDimension = 0x30;
        }
		public readonly partial struct CurrentScreenController
		{
			public const uint Generic = 0x20; // Var (UNKN)
		}

		public readonly partial struct OpticCameraManagerContainer
		{
			public const uint Instance = 0x0; // -.\uF124 (UNKN)
			public const uint OpticCameraManager = 0x10; // -.\uF125 (UNKN)
			public const uint FPSCamera = 0x60; // UnityEngine.Camera (UNKN)
		}
        public readonly partial struct Physical
        {
            public const uint Overweight = 0x1C;
            public const uint WalkOverweight = 0x20;
            public const uint WalkSpeedLimit = 0x24;
            public const uint Inertia = 0x28;
            public const uint Stamina = 0x68;
            public const uint Oxygen = 0x78;
            public const uint BaseOverweightLimits = 0xAC;
            public const uint SprintOverweightLimits = 0xC0;
			public const uint SprintWeightFactor = 0x104; // Single
            public const uint PreviousWeight = 0xD4;
            public const uint SprintAcceleration = 0x114;
            public const uint PreSprintAcceleration = 0x118;
			public const uint _encumbered = 0x11C;
            public const uint _overEncumbered = 0x11D;
			public const uint SprintOverweight = 0xD0;
			public const uint BerserkRestorationFactor = 0x110;
        }

        public readonly partial struct PhysicalValue //Class: .Stamina
        {
            public const uint Current = 0x10; // Single
        }

        public readonly partial struct BreathEffector //Class: EFT.Animations.BreathEffector
        {
            public const uint Intensity = 0x30; // Single <Intensity> Intensity
        }
		public readonly partial struct OpticCameraManager
		{
			public const uint Camera = 0x70; // UnityEngine.Camera (DEC 1)
			public const uint CurrentOpticSight = 0x78; // EFT.CameraControl.OpticSight (DEC 1)
		}

        public static class GPUInstancerManager
        {
            public const uint runtimeDataList = 0x58;
        }            
        public readonly partial struct GPUInstancerRuntimeData
        {
            public const uint instanceBounds = 0x20;
        }
		/// <summary>
		/// EFT.CameraControl.CameraManager - Main camera manager singleton (IL2CPP)
		/// </summary>
		public readonly partial struct EFTCameraManager
		{
			public const uint OpticCameraManager = 0x10; // UNCHANGED DEC 2025
			public const uint Camera = 0x60; // UnityEngine.Camera - FPS Camera (UNCHANGED DEC 2025)
            public const uint GetInstance_RVA = 0x105A4F0; // DEC 2025 - from Camera-PWA
            public const uint CameraDerefOffset = 0x10; // UNCHANGED DEC 2025 - dereference offset for Camera objects
		}

		public readonly partial struct OpticSight
		{
			public const uint LensRenderer = 0x20; // UnityEngine.Renderer (UNKN)
		}

		public readonly partial struct SightComponent
		{
			public const uint _template = 0x20; // -.\uEE6C (DEC 1)
			public const uint ScopesSelectedModes = 0x30; // System.Int32[] (DEC 1)
			public const uint SelectedScope = 0x38; // Int32 (DEC 1)
			public const uint ScopeZoomValue = 0x3C; // Single (DEC 1)
		}

		public readonly partial struct SightInterface
		{
			public const uint Zooms = 0x1B8; // System.Single[] 
		}
        public readonly partial struct WeatherController
        {
            public const uint WeatherDebug = 0x88;
        }             
        public readonly partial struct WeatherDebug
        {
            public const uint CloudDensity = 0x24;
            public const uint Fog = 0x28;
            public const uint LightningThunderProbability = 0x30;
            public const uint Rain = 0x2c;
            public const uint WindMagnitude = 0x14;
            public const uint isEnabled = 0x10;
        }		
        public static class Special
        {
            public const ulong TypeInfoTableRva = 0x6B3B5E8; // TYPE_INFO_TABLE //updated feb11

            public const uint FirearmController_WeaponDirectionGetterRva = 0x3CC77A0; // FirearmController.get_WeaponDirection_RVA 
            public const uint EFTHardSettings_TypeIndex = 225;
            public const uint EftScreenManager_TypeIndex = 15090;
            public const uint GPUInstancerManager_TypeIndex = 4915;
            public const uint WeatherController_TypeIndex = 10043;
            public const uint FirearmController_TypeIndex = 6934;
            public const uint LevelSettings_TypeIndex = 2052;
            public const uint GamePlayerOwner_TypeIndex = 8522;
            public const uint CameraManager_TypeIndex = 13107;
            public const uint ClientBackendSession_TypeIndex = 8151;
            public const uint EftClientBackendSession_TypeIndex = 8229;
            public const uint GlobalConfiguration_TypeIndex = 6404;

        }        
        public readonly partial struct Il2CppClass
        {
            // Existing:
            public const uint Name         = 0x10;
            public const uint Namespace    = 0x18;
            public const uint StaticFields = 0xB8;

            // NEW:
            public const uint Methods     = 0x98;  // Il2CppClass::methods
            public const uint MethodCount = 0x120; // Il2CppClass::method_count (uint16)
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
}