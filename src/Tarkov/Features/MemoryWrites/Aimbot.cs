using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.DMA;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.Features.Ballistics;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.Collections;
using System.Windows;
using eft_dma_radar.UI.Pages;
using Application = System.Windows.Application;
using eft_dma_radar.Common.Unity.LowLevel;
using eft_dma_radar.Common.DMA.ScatterAPI;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class Aimbot : MemWriteFeature<Aimbot>
    {
        #region Fields / Properties / Startup

        public static bool Engaged = false;

        /// <summary>
        /// Aimbot Configuration.
        /// </summary>
        public static AimbotConfig Config => Program.Config.MemWrites.Aimbot;
        /// <summary>
        /// Aimbot Supported Bones.
        /// </summary>
        public static readonly IReadOnlySet<Bones> BoneNames = new HashSet<Bones>
        {
            Bones.HumanHead,
            Bones.HumanNeck,
            Bones.HumanSpine3,
            Bones.HumanPelvis,
            Bones.Legs
        };

        private bool _firstLock;
        private sbyte _lastShotIndex = -1;
        private Bones _lastRandomBone = Config.Bone;
        private static bool _ballisticsDiagnosticLogged = false;
        /// <summary>
        /// Aimbot Info.
        /// </summary>
        public AimbotCache Cache { get; private set; }

        public Aimbot()
        {
            new Thread(AimbotWorker)
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest
            }.Start();
        }

        public override void OnGameStop()
        {
            _weaponDirectionGetter = null;
        }

        public override bool Enabled
        {
            get => Config.Enabled;
            set => Config.Enabled = value;
        }

        /// <summary>
        /// Managed Thread that does realtime Aimbot updates.
        /// </summary>
        private void AimbotWorker()
        {
            XMLogging.WriteLine("Aimbot thread starting...");
            while (true)
            {
                try
                {
                    
                    // Wait for raid signal (blocks until raid starts)
                    // WaitForRaid() returns true when OnRaidStarted() is called
                    // At that point Memory.Game is already set to a LocalGameWorld instance
                    if (MemDMABase.WaitForRaid() && Memory.Game is LocalGameWorld game)
                    {
                        // NOTE: We don't check game.RaidHasStarted anymore because it uses MonoLib
                        // which is deprecated and doesn't work in IL2CPP.
                        // If we're here, the raid is definitely active.
                        // Run ballistics diagnostic once per raid (even without MemWrites enabled)
                        TryRunBallisticsDiagnostic();
                        
                        // ALWAYS log on first iteration after raid starts (per iteration check)
                        //XMLogging.WriteLine($"[Aimbot] WORKER CHECK: Enabled={Enabled}, MemWrites.Enabled={MemWrites.Enabled}, Engaged={Engaged}");
                        
                        // Only run aimbot if enabled AND MemWrites enabled
                        if (Enabled && MemWrites.Enabled)
                        {
                            while (Enabled && MemWrites.Enabled && game.InRaid)
                            {
                                SetAimbot(game);
                            }
                        }
                        else
                        {
                            // Aimbot disabled - sleep and retry
                            Thread.Sleep(500);
                        }
                    }
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"CRITICAL ERROR on Aimbot Thread: {ex}"); // Log CRITICAL error
                }
                finally
                {
                    try { ResetAimbot(); } catch { }
                    Thread.Sleep(200);
                }
            }
        }
        
        /// <summary>
        /// Public static method to run ballistics diagnostic - can be called from LocalGameWorld.
        /// </summary>
        public static void RunBallisticsDiagnosticOnce()
        {
            if (_ballisticsDiagnosticLogged) return;
            
            XMLogging.WriteLine("[Aimbot] RunBallisticsDiagnosticOnce called from LocalGameWorld...");
            
            try
            {
                // Wait a bit for the realtime thread to populate HandsController
                Thread.Sleep(2000);
                
                var handsController = ILocalPlayer.HandsController;
                if (handsController == 0 || !handsController.IsValidVirtualAddress())
                {
                    // Wait up to 8 more seconds
                    for (int i = 0; i < 16; i++)
                    {
                        Thread.Sleep(500);
                        handsController = ILocalPlayer.HandsController;
                        if (handsController != 0 && handsController.IsValidVirtualAddress())
                        {
                            XMLogging.WriteLine($"[Aimbot] HandsController ready after {2000 + (i+1)*500}ms");
                            break;
                        }
                    }
                }
                
                if (handsController == 0 || !handsController.IsValidVirtualAddress())
                {
                    XMLogging.WriteLine("[Aimbot] Diagnostic skipped - HandsController not ready (equip a weapon!)");
                    _ballisticsDiagnosticLogged = true;
                    return;
                }
                
                XMLogging.WriteLine($"[Aimbot] HandsController @ 0x{handsController:X} - running diagnostic...");
                LogBallisticsDiagnostic(handsController, null);
                _ballisticsDiagnosticLogged = true;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Aimbot] Diagnostic error: {ex.Message}");
                _ballisticsDiagnosticLogged = true;
            }
        }
        
        /// <summary>
        /// Runs ballistics diagnostic once per raid when a weapon is equipped.
        /// Works even if MemWrites are disabled.
        /// </summary>
        private void TryRunBallisticsDiagnostic()
        {
            if (_ballisticsDiagnosticLogged) return;
            
            XMLogging.WriteLine("[Aimbot] TryRunBallisticsDiagnostic starting...");
            
            try
            {
                if (Memory.LocalPlayer is not LocalPlayer localPlayer)
                {
                    XMLogging.WriteLine("[Aimbot] Diagnostic: LocalPlayer is null");
                    return;
                }
                
                XMLogging.WriteLine("[Aimbot] Diagnostic: Waiting for HandsController...");
                
                // HandsController is set by OnRealtimeLoop - may take a few seconds after raid start
                var handsController = ILocalPlayer.HandsController;
                if (handsController == 0 || !handsController.IsValidVirtualAddress())
                {
                    // Wait up to 10 seconds for HandsController to be populated
                    for (int i = 0; i < 20; i++)
                    {
                        Thread.Sleep(500);
                        handsController = ILocalPlayer.HandsController;
                        if (handsController != 0 && handsController.IsValidVirtualAddress())
                        {
                            XMLogging.WriteLine($"[Aimbot] Diagnostic: HandsController ready after {(i+1)*500}ms");
                            break;
                        }
                    }
                    
                    if (handsController == 0 || !handsController.IsValidVirtualAddress())
                    {
                        XMLogging.WriteLine("[Aimbot] Diagnostic skipped - HandsController not ready after 10s (no weapon equipped?)");
                        _ballisticsDiagnosticLogged = true; // Don't retry
                        return;
                    }
                }
                else
                {
                    XMLogging.WriteLine($"[Aimbot] Diagnostic: HandsController already ready @ 0x{handsController:X}");
                }
                
                XMLogging.WriteLine("[Aimbot] Weapon detected - running ballistics diagnostic...");
                LogBallisticsDiagnostic(handsController, null);
                _ballisticsDiagnosticLogged = true;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Aimbot] Diagnostic failed: {ex.Message}");
                _ballisticsDiagnosticLogged = true; // Don't spam retries on error
            }
        }

        #endregion

        #region Aimbot Execution

        /// <summary>
        /// Executes Aimbot features on the AimbotDMAWorker Thread.
        /// </summary>
        private void SetAimbot(LocalGameWorld game)
        {            
            try
            {
                // Check for weapon equip even without aimbot engaged (for diagnostics)
                if (Memory.LocalPlayer is LocalPlayer localPlayer && ILocalPlayer.HandsController is ulong handsController && handsController.IsValidVirtualAddress())
                {
                    // One-time ballistics diagnostic log - runs on first weapon equip regardless of aimbot state
                    if (!_ballisticsDiagnosticLogged)
                    {
                        XMLogging.WriteLine("[Aimbot] Weapon detected - running ballistics diagnostic...");
                        LogBallisticsDiagnostic(handsController, null);
                        _ballisticsDiagnosticLogged = true;
                    }
                }
                
                if (Engaged && Memory.LocalPlayer is LocalPlayer lp && ILocalPlayer.HandsController is ulong hc && hc.IsValidVirtualAddress())
                {
                    if (Cache != hc)
                    {
                        XMLogging.WriteLine("[Aimbot] ENGAGED - Initializing cache...");
                        Cache?.ResetLock();
                        Cache = new AimbotCache(hc);

                        const float targetAccuracy = 0.0003f;
                        var currentAccuracy = Memory.ReadValue<float>(hc + Offsets.FirearmController.TotalCenterOfImpact);
                        if (currentAccuracy != targetAccuracy &&
                            currentAccuracy > 0f && currentAccuracy < 1f)
                        {
                            Memory.WriteValue(game, hc + Offsets.FirearmController.TotalCenterOfImpact, targetAccuracy);
                            XMLogging.WriteLine($"[Aimbot] Set Weapon Accuracy {currentAccuracy} -> {targetAccuracy}");
                        }
                    }

                    if (Cache is null)
                    {
                        Thread.Sleep(1);
                        return;
                    }

                    Cache.FireportTransform ??= GetFireport(hc);

                    if (Cache.AimbotLockedPlayer is not null)
                    {
                        ulong corpseAddr = Cache.AimbotLockedPlayer.CorpseAddr;
                        if (corpseAddr.IsValidVirtualAddress())
                        {
                            ulong corpse = Memory.ReadValue<ulong>(corpseAddr, false);
                            if (corpse.IsValidVirtualAddress()) // Dead
                            {
                                Cache.AimbotLockedPlayer.SetDead(corpse);
                                Cache.ResetLock();
                            }
                        }
                    }

                    if (Cache.AimbotLockedPlayer is null)
                    {
                        if (_firstLock && Config.DisableReLock)
                        {
                            ResetAimbot();
                            while (Engaged)
                                Thread.Sleep(1);
                            return;
                        }

                        Cache.AimbotLockedPlayer = GetBestAimbotTarget(game, lp);
                        
                        if (Cache.AimbotLockedPlayer is null)
                        {
                            // Log why no target (only once per engage cycle)
                            var hostilePlayers = game.Players?.Where(x => x.IsHostileActive && x is not BtrOperator).ToList();
                            if (hostilePlayers is null || hostilePlayers.Count == 0)
                            {
                                // Don't spam - only log occasionally
                            }
                        }
                    }

                    if (Cache.AimbotLockedPlayer is null)
                    {
                        Thread.Sleep(1);
                        return;
                    }

                    // Only log once when first locking onto a target
                    if (!_firstLock)
                    {
                        XMLogging.WriteLine($"[Aimbot] LOCKED onto {Cache.AimbotLockedPlayer.Name}!");
                        _firstLock = true;
                    }
                    BeginSilentAim(null, lp);
                    Cache.AimbotLockedPlayer.IsAimbotLocked = true;
                }
                else if (Engaged)
                {
                    // Engaged but conditions not met
                    var lpValid = Memory.LocalPlayer is LocalPlayer;
                    var hcAddr = ILocalPlayer.HandsController;
                    XMLogging.WriteLine($"[Aimbot] Engaged but can't run: LocalPlayer={lpValid}, HandsController=0x{hcAddr:X}");
                    Thread.Sleep(100);
                }
                else
                {
                    _firstLock = false;
                    ResetAimbot();
                    Thread.Sleep(1);
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Aimbot [FAIL] {ex}");
            }
        }

        /// <summary>
        /// Begin Silent Aim Aimbot.
        /// </summary>
        private void BeginSilentAim(LocalGameWorld game, LocalPlayer localPlayer)
        {
            try
            {
                var writeHandle = new ScatterWriteHandle();
                var target = Cache.AimbotLockedPlayer;
                var bone = Config.Bone;

                if (Config.HeadshotAI && target.IsAI)
                    bone = Bones.HumanHead;

                if (MemWriteFeature<RageMode>.Instance.Enabled || Config.HeadshotAI && target.IsAI)
                    bone = Bones.HumanHead;
                    
                else if (Config.RandomBone.Enabled) // Random Bone
                {
                    var shotIndex = Memory.ReadValue<sbyte>(Cache + Offsets.ClientFirearmController.ShotIndex, false);
                    if (shotIndex != _lastShotIndex)
                    {
                        _lastRandomBone = Config.RandomBone.GetRandomBone();
                        _lastShotIndex = shotIndex;
                        XMLogging.WriteLine($"New Random Bone {_lastRandomBone.GetDescription()} ({shotIndex})");
                    }
                    bone = _lastRandomBone;
                }
                else if (Config.SilentAim.AutoBone)
                {
                    var boneTargets = new List<PossibleAimbotTarget>();
                    foreach (var tr in target.Skeleton.Bones)
                    {
                        if (tr.Key is Bones.HumanBase)
                            continue;
                        if (CameraManagerBase.WorldToScreen(ref tr.Value.Position, out var scrPos, true))
                        {
                            boneTargets.Add(
                            new PossibleAimbotTarget()
                            {
                                Player = target,
                                FOV = CameraManagerBase.GetFovMagnitude(scrPos),
                                Bone = tr.Key
                            });
                        }
                    }
                    if (boneTargets.Count > 0)
                        bone = boneTargets.MinBy(x => x.FOV).Bone;
                }
                if (bone == Bones.Legs) // Pick a leg
                {
                    bool isLeft = Random.Shared.Next(0, 2) == 1;
                    if (isLeft)
                        bone = Bones.HumanLThigh2;
                    else
                        bone = Bones.HumanRThigh2;
                }

                /// Target Bone Position
                Vector3 bonePosition = target.Skeleton.Bones[bone].UpdatePosition();

                if (Config.SilentAim.SafeLock)
                {
                    if (IsSafeLockTripped()) // Unlock if target has left FOV
                    {
                        _firstLock = false; // Allow re-lock
                        ResetAimbot();
                        return;
                    }
                    bool IsSafeLockTripped()
                    {
                        foreach (var tr in target.Skeleton.Bones)
                        {
                            if (tr.Key is Bones.HumanBase)
                                continue;
                            if (CameraManagerBase.WorldToScreen(ref tr.Value.Position, out var scrPos, true) &&
                                CameraManagerBase.GetFovMagnitude(scrPos) is float fov && fov < Config.FOV)
                                return false; // At least one bone in FOV - exit early
                        }
                        return true;
                    }
                }

                /// Get Fireport Position & Run Prediction
                Vector3 fireportPosition;
                try
                {
                    fireportPosition = Cache.FireportTransform.UpdatePosition();
                }
                catch
                {
                    Cache.FireportTransform = null;
                    throw;
                }

                Vector3 newWeaponDirection = CalculateSilentAimTrajectory(target, ref fireportPosition, ref bonePosition);
                newWeaponDirection.ThrowIfAbnormal();
                Cache.CurrentTargetBone = bone;
                Cache.CurrentTargetBonePos = bonePosition; // predicted
                ulong pwa = localPlayer.PWA;
                if (pwa.IsValidVirtualAddress())
                {
                    Cache?.CaptureOriginalShotSettings(pwa);

                    // Disable PWA FOV adjust as before
                    Memory.WriteValue(
                        pwa + Offsets.ProceduralWeaponAnimation.ShotNeedsFovAdjustments,
                        false);
                }
                Memory.WriteValue(localPlayer.PWA + Offsets.ProceduralWeaponAnimation.ShotNeedsFovAdjustments, false);
                
                // DATA-BASED SILENT AIM: Write directly to _shotDirection field instead of patching code
                // This is safer because it writes to heap memory, not executable code
                WriteShotDirection(localPlayer.PWA, newWeaponDirection, Cache.FireportTransform);
                
                Cache.LastFireportPos = fireportPosition;

                Cache.LastPlayerPos = bonePosition; // keep for legacy / safety            
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Silent Aim [FAIL] {ex}");
                ResetSilentAim();
            }
        }

        #endregion

        #region Helper Methods
        private static ScatterWriteHandle writes;
        private static Player GetBestAimbotTarget(LocalGameWorld game, Player localPlayer)
        {
            var players = game.Players?
                .Where(x => x.IsHostileActive && x is not BtrOperator);

            if (players is null || !players.Any())
                return null;

            // Calculate fov, distance and build Target Collection
            var targets = new List<PossibleAimbotTarget>();
            foreach (var player in players)
            {
                var distance = Vector3.Distance(localPlayer.Position, player.Position);
                if (distance > MemWrites.Config.Aimbot.Distance)
                    continue;

                foreach (var tr in player.Skeleton.Bones)
                {
                    if (tr.Key is Bones.HumanBase)
                        continue;
                    if (CameraManagerBase.WorldToScreen(ref tr.Value.Position, out var scrPos, true) &&
                        CameraManagerBase.GetFovMagnitude(scrPos) is float fov && fov < Config.FOV)
                    {
                        var target = new PossibleAimbotTarget()
                        {
                            Player = player,
                            FOV = fov,
                            Distance = Vector3.Distance(localPlayer.Position, tr.Value.Position)
                        };                     
                        targets.Add(target);
                    }
                }
            }

            if (targets.Count == 0)
                return null;
            switch (Config.TargetingMode)
            {
                case AimbotTargetingMode.FOV:
                    return targets.MinBy(x => x.FOV).Player;
                case AimbotTargetingMode.CQB:
                    return targets.MinBy(x => x.Distance).Player;
                default:
                    throw new NotImplementedException(nameof(Config.TargetingMode));
            }
        }

        /// <summary>
        /// Get LocalPlayer Fireport Transform.
        /// </summary>
        /// <param name="handsController"></param>
        /// <returns></returns>
        private static UnityTransform GetFireport(ulong handsController)
        {
            handsController.ThrowIfInvalidVirtualAddress();
            var ti = Memory.ReadPtrChain(handsController, Offsets.FirearmController.To_FirePortTransformInternal, false);
            return new UnityTransform(ti);
        }

        /// <summary>
        /// One-time diagnostic log of all ballistics-related offsets and values.
        /// Verifies pointer chains and offset values are correct for IL2CPP.
        /// </summary>
        private static void LogBallisticsDiagnostic(ulong handsController, AimbotCache cache)
        {
            try
            {
                XMLogging.WriteLine("================================================================");
                XMLogging.WriteLine("         AIMBOT BALLISTICS DIAGNOSTIC (ONE-TIME)               ");
                XMLogging.WriteLine("================================================================");
                
                // === POINTER CHAIN VERIFICATION ===
                XMLogging.WriteLine("[DIAG] === POINTER CHAINS ===");
                XMLogging.WriteLine($"[DIAG] HandsController @ 0x{handsController:X}");
                
                // Fireport chain: [0x150, 0x10, 0x10]
                var fireportPtr = Memory.ReadPtr(handsController + Offsets.FirearmController.Fireport, false);
                XMLogging.WriteLine($"[DIAG] Fireport (0x{Offsets.FirearmController.Fireport:X}) -> 0x{fireportPtr:X} {(fireportPtr.IsValidVirtualAddress() ? "OK" : "FAIL")}");
                
                if (fireportPtr.IsValidVirtualAddress())
                {
                    var originalTransform = Memory.ReadPtr(fireportPtr + 0x10, false);
                    XMLogging.WriteLine($"[DIAG]   +0x10 (Original) -> 0x{originalTransform:X} {(originalTransform.IsValidVirtualAddress() ? "OK" : "FAIL")}");
                    
                    if (originalTransform.IsValidVirtualAddress())
                    {
                        var transformInternal = Memory.ReadPtr(originalTransform + 0x10, false);
                        XMLogging.WriteLine($"[DIAG]   +0x10 (TransformInternal) -> 0x{transformInternal:X} {(transformInternal.IsValidVirtualAddress() ? "OK" : "FAIL")}");
                        
                        // Get position from TransformInternal
                        if (transformInternal.IsValidVirtualAddress())
                        {
                            var fireport = new UnityTransform(transformInternal);
                            _ = fireport.UpdatePosition();
                            XMLogging.WriteLine($"[DIAG]   Fireport Position: {fireport.Position}");
                        }
                    }
                }
                
                // === OFFSET VALUES ===
                XMLogging.WriteLine("[DIAG] === OFFSET VALUES ===");
                XMLogging.WriteLine($"[DIAG] FirearmController.TotalCenterOfImpact: 0x{Offsets.FirearmController.TotalCenterOfImpact:X}");
                XMLogging.WriteLine($"[DIAG] FirearmController.Fireport: 0x{Offsets.FirearmController.Fireport:X}");
                XMLogging.WriteLine($"[DIAG] ClientFirearmController.ShotIndex: 0x{Offsets.ClientFirearmController.ShotIndex:X}");
                XMLogging.WriteLine($"[DIAG] ItemHandsController.Item: 0x{Offsets.ItemHandsController.Item:X}");
                XMLogging.WriteLine($"[DIAG] LootItem.Template: 0x{Offsets.LootItem.Template:X}");
                XMLogging.WriteLine($"[DIAG] LootItem.Version: 0x{Offsets.LootItem.Version:X}");
                XMLogging.WriteLine($"[DIAG] AmmoTemplate.InitialSpeed: 0x{Offsets.AmmoTemplate.InitialSpeed:X}");
                XMLogging.WriteLine($"[DIAG] AmmoTemplate.BallisticCoeficient: 0x{Offsets.AmmoTemplate.BallisticCoeficient:X}");
                XMLogging.WriteLine($"[DIAG] AmmoTemplate.BulletMassGram: 0x{Offsets.AmmoTemplate.BulletMassGram:X}");
                XMLogging.WriteLine($"[DIAG] AmmoTemplate.BulletDiameterMilimeters: 0x{Offsets.AmmoTemplate.BulletDiameterMilimeters:X}");
                XMLogging.WriteLine($"[DIAG] WeaponTemplate.Velocity: 0x{Offsets.WeaponTemplate.Velocity:X}");
                XMLogging.WriteLine($"[DIAG] ModTemplate.Velocity: 0x{Offsets.ModTemplate.Velocity:X}");
                XMLogging.WriteLine($"[DIAG] LootItemMod.Slots: 0x{Offsets.LootItemMod.Slots:X}");
                XMLogging.WriteLine($"[DIAG] Slot.ContainedItem: 0x{Offsets.Slot.ContainedItem:X}");
                XMLogging.WriteLine($"[DIAG] ObservedMovementController.Velocity: 0x{Offsets.ObservedMovementController.Velocity:X}");
                
                // === READ LIVE VALUES ===
                XMLogging.WriteLine("[DIAG] === LIVE VALUES ===");
                
                var coi = Memory.ReadValue<float>(handsController + Offsets.FirearmController.TotalCenterOfImpact, false);
                XMLogging.WriteLine($"[DIAG] TotalCenterOfImpact: {coi} {(coi > 0 && coi < 1 ? "OK" : "CHECK")}");
                
                var shotIndex = Memory.ReadValue<sbyte>(handsController + Offsets.ClientFirearmController.ShotIndex, false);
                XMLogging.WriteLine($"[DIAG] ShotIndex: {shotIndex}");
                
                // Item chain
                var itemBase = Memory.ReadPtr(handsController + Offsets.ItemHandsController.Item, false);
                XMLogging.WriteLine($"[DIAG] ItemBase @ 0x{itemBase:X} {(itemBase.IsValidVirtualAddress() ? "OK" : "FAIL")}");
                
                if (itemBase.IsValidVirtualAddress())
                {
                    var itemTemplate = Memory.ReadPtr(itemBase + Offsets.LootItem.Template, false);
                    XMLogging.WriteLine($"[DIAG] ItemTemplate @ 0x{itemTemplate:X} {(itemTemplate.IsValidVirtualAddress() ? "OK" : "FAIL")}");
                    
                    var weaponVersion = Memory.ReadValue<int>(itemBase + Offsets.LootItem.Version, false);
                    XMLogging.WriteLine($"[DIAG] WeaponVersion: {weaponVersion}");
                    
                    if (itemTemplate.IsValidVirtualAddress())
                    {
                        var weaponVelocity = Memory.ReadValue<float>(itemTemplate + Offsets.WeaponTemplate.Velocity, false);
                        XMLogging.WriteLine($"[DIAG] WeaponTemplate.Velocity: {weaponVelocity}%");
                    }
                    
                    // Try to get ammo template
                    try
                    {
                        var ammoTemplate = FirearmManager.MagazineManager.GetAmmoTemplateFromWeapon(itemBase);
                        if (ammoTemplate.IsValidVirtualAddress())
                        {
                            XMLogging.WriteLine($"[DIAG] AmmoTemplate @ 0x{ammoTemplate:X}");
                            
                            var initialSpeed = Memory.ReadValue<float>(ammoTemplate + Offsets.AmmoTemplate.InitialSpeed, false);
                            var ballisticCoef = Memory.ReadValue<float>(ammoTemplate + Offsets.AmmoTemplate.BallisticCoeficient, false);
                            var bulletMass = Memory.ReadValue<float>(ammoTemplate + Offsets.AmmoTemplate.BulletMassGram, false);
                            var bulletDiameter = Memory.ReadValue<float>(ammoTemplate + Offsets.AmmoTemplate.BulletDiameterMilimeters, false);
                            
                            XMLogging.WriteLine($"[DIAG]   InitialSpeed: {initialSpeed} m/s {(initialSpeed > 100 && initialSpeed < 2000 ? "OK" : "CHECK")}");
                            XMLogging.WriteLine($"[DIAG]   BallisticCoef: {ballisticCoef} {(ballisticCoef > 0 && ballisticCoef < 2 ? "OK" : "CHECK")}");
                            XMLogging.WriteLine($"[DIAG]   BulletMass: {bulletMass}g {(bulletMass > 0 && bulletMass < 100 ? "OK" : "CHECK")}");
                            XMLogging.WriteLine($"[DIAG]   BulletDiameter: {bulletDiameter}mm {(bulletDiameter > 1 && bulletDiameter < 30 ? "OK" : "CHECK")}");
                            
                            // Calculate total velocity with attachments
                            float velMod = 0f;
                            velMod += Memory.ReadValue<float>(itemTemplate + Offsets.WeaponTemplate.Velocity, false);
                            
                            // This is expensive so just log the base
                            var totalVelMod = 1f + (velMod / 100f);
                            var calculatedSpeed = initialSpeed * totalVelMod;
                            XMLogging.WriteLine($"[DIAG]   Calculated Muzzle Velocity: ~{calculatedSpeed:F1} m/s (without attachments)");
                        }
                        else
                        {
                            XMLogging.WriteLine("[DIAG] AmmoTemplate: NOT LOADED (no round in chamber?)");
                        }
                    }
                    catch (Exception ex)
                    {
                        XMLogging.WriteLine($"[DIAG] AmmoTemplate read failed: {ex.Message}");
                    }
                }
                
                // === VELOCITY OFFSET TEST ===
                LogTargetVelocityDiagnostic();
                
                XMLogging.WriteLine("[DIAG] === END BALLISTICS DIAGNOSTIC ===");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[DIAG] Ballistics diagnostic failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Logs velocity values from nearby ObservedPlayers to verify the offset is correct.
        /// Valid velocities should be in range -10 to +10 m/s per axis.
        /// </summary>
        private static void LogTargetVelocityDiagnostic()
        {
            try
            {
                XMLogging.WriteLine("[DIAG] === TARGET VELOCITY TEST ===");
                XMLogging.WriteLine($"[DIAG] ObservedMovementController.Velocity offset: 0x{Offsets.ObservedMovementController.Velocity:X}");
                XMLogging.WriteLine($"[DIAG] ObservedPlayerController.MovementController chain: [0x{Offsets.ObservedPlayerController.MovementController[0]:X}, 0x{Offsets.ObservedPlayerController.MovementController[1]:X}]");
                
                if (Memory.Game is not LocalGameWorld game)
                {
                    XMLogging.WriteLine("[DIAG] No game instance");
                    return;
                }
                
                var players = game.Players?.Where(x => x is ObservedPlayer && x.IsActive).Take(3).ToList();
                if (players == null || players.Count == 0)
                {
                    XMLogging.WriteLine("[DIAG] No ObservedPlayers found to test velocity");
                    return;
                }
                
                XMLogging.WriteLine($"[DIAG] Testing velocity on {players.Count} player(s):");
                
                foreach (var player in players)
                {
                    try
                    {
                        var movementContext = player.MovementContext;
                        if (movementContext == 0)
                        {
                            XMLogging.WriteLine($"[DIAG]   {player.Name}: MovementContext is NULL");
                            continue;
                        }
                        
                        var velocity = Memory.ReadValue<Vector3>(movementContext + Offsets.ObservedMovementController.Velocity, false);
                        
                        bool isValid = Math.Abs(velocity.X) < 25f && Math.Abs(velocity.Y) < 25f && Math.Abs(velocity.Z) < 25f;
                        bool isMoving = Math.Abs(velocity.X) > 0.1f || Math.Abs(velocity.Y) > 0.1f || Math.Abs(velocity.Z) > 0.1f;
                        
                        XMLogging.WriteLine($"[DIAG]   {player.Name}:");
                        XMLogging.WriteLine($"[DIAG]     MovementContext: 0x{movementContext:X}");
                        XMLogging.WriteLine($"[DIAG]     Velocity: X={velocity.X:F2}, Y={velocity.Y:F2}, Z={velocity.Z:F2}");
                        XMLogging.WriteLine($"[DIAG]     Speed: {velocity.Length():F2} m/s");
                        XMLogging.WriteLine($"[DIAG]     Valid: {(isValid ? "OK" : "FAIL - garbage values!")} | Moving: {(isMoving ? "YES" : "STATIONARY")}");
                    }
                    catch (Exception ex)
                    {
                        XMLogging.WriteLine($"[DIAG]   {player.Name}: Error reading velocity - {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[DIAG] Target velocity test failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Recurses a given weapon for the total velocity on attachments.
        /// Used by Aimbot.
        /// </summary>
        /// <param name="lootItemBase">Item (Weapon) to recurse.</param>
        /// <param name="velocityModifier">Percentage to adjust the base velocity of a muzzle by.</param>
        private static void RecurseWeaponAttachVelocity(ulong lootItemBase, ref float velocityModifier)
        {
            try
            {
                var parentSlots = Memory.ReadPtr(lootItemBase + Offsets.LootItemMod.Slots);
                using var slots = MemArray<ulong>.Get(parentSlots);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(slots.Count, 100, nameof(slots));

                foreach (var slot in slots)
                {
                    try
                    {
                        var containedItem = Memory.ReadPtr(slot + Offsets.Slot.ContainedItem);
                        var itemTemplate = Memory.ReadPtr(containedItem + Offsets.LootItem.Template);
                        // Add this attachment's Velocity %
                        velocityModifier += Memory.ReadValue<float>(itemTemplate + Offsets.ModTemplate.Velocity);
                        RecurseWeaponAttachVelocity(containedItem, ref velocityModifier);
                    }
                    catch
                    {
                    } // Skip over empty slots
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"AIMBOT ERROR RecurseWeaponAttachVelocity() -> {ex}");
            }
        }

        /// <summary>
        /// Runs Aimbot Prediction between a source -> target.
        /// </summary>
        /// <param name="target">Target player.</param>
        /// <param name="sourcePosition">Source position.</param>
        /// <param name="targetPosition">Target position.</param>
        /// <returns>Weapon direction for the Source Position to aim towards the Target Position accounting for prediction results.</returns>
        private Vector3 CalculateSilentAimTrajectory(Player target, ref Vector3 sourcePosition, ref Vector3 targetPosition)
        {
            /// Get Current Ammo Details
            try
            {
                // Chambered bullet's velocity - this needs to be updated independently of the aimbot to improve performance

                int weaponVersion = Memory.ReadValue<int>(Cache.ItemBase + Offsets.LootItem.Version);
                if (Cache.LastWeaponVersion != weaponVersion) // New round in chamber
                {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            LootFilterControl.CreateWeaponAmmoGroup();
                        });
                    var ammoTemplate = FirearmManager.MagazineManager.GetAmmoTemplateFromWeapon(Cache.ItemBase);
                    if (Cache.LoadedAmmo != ammoTemplate)
                    {                        
                        XMLogging.WriteLine("[Aimbot] Ammo changed!");
                        Cache.Ballistics.BulletMassGrams = Memory.ReadValue<float>(ammoTemplate + Offsets.AmmoTemplate.BulletMassGram);
                        Cache.Ballistics.BulletDiameterMillimeters =
                            Memory.ReadValue<float>(ammoTemplate + Offsets.AmmoTemplate.BulletDiameterMilimeters);
                        Cache.Ballistics.BallisticCoefficient =
                            Memory.ReadValue<float>(ammoTemplate + Offsets.AmmoTemplate.BallisticCoeficient);

                        /// Calculate Muzzle Velocity. There is a base value based on the Ammo Type,
                        /// however certain attachments/barrels will apply a % modifier to that base value.
                        /// These calculations will get the correct value.
                        float bulletSpeed = Memory.ReadValue<float>(ammoTemplate + Offsets.AmmoTemplate.InitialSpeed);
                        float velMod = 0f;
                        velMod += Memory.ReadValue<float>(Cache.ItemTemplate + Offsets.WeaponTemplate.Velocity);
                        RecurseWeaponAttachVelocity(Cache.ItemBase, ref velMod); // Expensive operation
                        velMod = 1f + (velMod / 100f); // Get percentage (the game will give us 15.00, we want to turn it into 1.15)
                        // Integrity check -> Should be between 0.01 and 1.99
                        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(velMod, 0d, nameof(velMod));
                        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(velMod, 2d, nameof(velMod));
                        bulletSpeed *= velMod;
                        // Calcs OK -> Cache Weapon/Ammo
                        Cache.Ballistics.BulletSpeed = bulletSpeed;
                        Cache.LoadedAmmo = ammoTemplate;
                    }
                    Cache.LastWeaponVersion = weaponVersion;
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Aimbot [WARNING] - Unable to set/update Ballistics: {ex}");
            }
            /// Target Velocity - Read from appropriate source based on player type
            Vector3 targetVelocity = Vector3.Zero;
            bool velocityValid = false;
            
            if (target is ObservedPlayer observedPlayer && target.MovementContext.IsValidVirtualAddress())
            {
                // ObservedPlayer: Read from MovementContext -> ObservedMovementController.Velocity
                try
                {
                    targetVelocity = Memory.ReadValue<Vector3>(target.MovementContext + Offsets.ObservedMovementController.Velocity, false);
                    float speed = targetVelocity.Length();
                    // Validate: realistic speed is 0.1-15 m/s
                    if (speed >= 0.1f && speed <= 15f)
                    {
                        velocityValid = true;
                    }
                }
                catch { }
            }
            else if (target is ClientPlayer clientPlayer && target.MovementContext.IsValidVirtualAddress())
            {
                // ClientPlayer: Read from SimpleCharacterController._velocity
                // Chain: Player._characterController (0x40) -> SimpleCharacterController._velocity (0xF0)
                try
                {
                    var charController = Memory.ReadPtr(target.Base + 0x40, false);
                    if (charController.IsValidVirtualAddress())
                    {
                        targetVelocity = Memory.ReadValue<Vector3>(charController + 0xF0, false);
                        float speed = targetVelocity.Length();
                        // Validate: realistic speed is 0.5-15 m/s, not normalized (~1.0)
                        if (speed > 0.5f && speed < 15f && Math.Abs(speed - 1.0f) > 0.1f)
                        {
                            velocityValid = true;
                        }
                    }
                }
                catch { }
            }
            
            /// Run Prediction Simulation
            if (Cache.IsAmmoValid)
            {
                var sim = BallisticsSimulation.Run(ref sourcePosition, ref targetPosition, Cache.Ballistics);
                
                if (velocityValid)
                {
                    // Apply lead prediction: velocity * travel time
                    targetVelocity *= sim.TravelTime;
                    
                    targetPosition.X += targetVelocity.X;
                    targetPosition.Y += targetVelocity.Y + sim.DropCompensation; // Lead Y + Drop combined
                    targetPosition.Z += targetVelocity.Z;
                    
                    // Log velocity usage (throttled to avoid spam)
                    if (Cache.LastVelocityLogTime == 0 || (DateTime.UtcNow.Ticks - Cache.LastVelocityLogTime) / TimeSpan.TicksPerMillisecond >= 1000)
                    {
                        float targetSpeed = targetVelocity.Length() / sim.TravelTime; // Original speed
                        XMLogging.WriteLine($"[Aimbot] Lead applied: {targetSpeed:F1}m/s, Travel={sim.TravelTime*1000:F0}ms, Drop={sim.DropCompensation:F2}m");
                        Cache.LastVelocityLogTime = DateTime.UtcNow.Ticks;
                    }
                }
                else
                {
                    // No velocity - just apply drop
                    targetPosition.Y += sim.DropCompensation;
                }
            }
            else
            {
                Cache.LoadedAmmo = default;
                Cache.LastWeaponVersion = default;
                XMLogging.WriteLine("Aimbot [WARNING] - Invalid Ammo Ballistics! Running without prediction.");
            }

            return Vector3.Normalize(targetPosition - sourcePosition); // Return direction
        }

        /// <summary>
        /// Reset the Aimbot Lock and reset the aimbot back to default state.
        /// </summary>
        private void ResetAimbot()
        {
            try
            {
                Cache?.ResetLock();
                ResetSilentAim();
                // Clear mem-write state on the current local player if available
                if (Memory.LocalPlayer is LocalPlayer lp)
                    ClearShotDirection(lp);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Aimbot] ResetAimbot error: {ex}");
            }
        
            Cache = null;
            _lastShotIndex = -1;
            Cache?.OriginalShotDirection = null;
            Cache?.OriginalShotNeedsFovAdjust = null;            
        }
        /// <summary>
        /// Restore the original shot direction / FOV flag for the current PWA.
        /// Uses WriteValueEnsure so we don't leave it in a half-written state.
        /// </summary>
        private void ClearShotDirection(LocalPlayer localPlayer)
        {
            if(!MemWrites.Config.MemWritesEnabled)
                return;
            try
            {
                if (localPlayer is null)
                    return;
        
                ulong pwa = localPlayer.PWA;
                if (!pwa.IsValidVirtualAddress())
                    return;
        
                // Make sure defaults are captured (in case we somehow got here before ApplyTransformSilentAim)
                Cache?.CaptureOriginalShotSettings(pwa);
        
                var shotDirAddr = pwa + Offsets.ProceduralWeaponAnimation._shotDirection;
                var fovFlagAddr = pwa + Offsets.ProceduralWeaponAnimation.ShotNeedsFovAdjustments;
        
                // Restore original shot direction if we have it
                if (Cache != null && Cache.OriginalShotDirection.HasValue)
                {
                    var originalDir = Cache.OriginalShotDirection.Value;
                    Memory.WriteValueEnsure(shotDirAddr, originalDir);
                }
                else
                {
                    // Fallback: re-read current value and rewrite it with ensure just to sanitize
                    var currentDir = Memory.ReadValue<Vector3>(shotDirAddr, false);
                    Memory.WriteValueEnsure(shotDirAddr, currentDir);
                }
        
                // Restore original FOV adjust flag if known; fall back to true (vanilla)
                if (Cache != null && Cache.OriginalShotNeedsFovAdjust.HasValue)
                {
                    Memory.WriteValueEnsure(fovFlagAddr, Cache.OriginalShotNeedsFovAdjust.Value);
                }
                else
                {
                    Memory.WriteValueEnsure(fovFlagAddr, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Aimbot] ClearShotDirection error: {ex}");
            }
        }
        #endregion

        #region Silent Aim Internal

        private static ulong? _weaponDirectionGetter;       
        private static long _lastPatchTicks = 0;
        private static Vector3 _lastPatchedDirection = Vector3.Zero;
        
        /// <summary>
        /// DRY RUN MODE: When true, logs patch data but does NOT write to memory.
        /// Set to false only after verifying the patch bytes are correct!
        /// </summary>
        private const bool SILENT_AIM_DRY_RUN = false; // LIVE MODE - Data-based silent aim (PWA method)
        
        /// <summary>
        /// Minimum milliseconds between patch writes to prevent DMA flooding.
        /// </summary>
        private const int PATCH_THROTTLE_MS = 50;
        
        private static bool _shotDirectionDiagLogged = false;
        private static long _lastDryRunLogTicks = 0;
        private static bool _shotDirectionNeutral = true; // Track if we're writing (false) or letting game handle it (true)     
  
        /// <summary>
        /// DATA-BASED SILENT AIM: Write directly to _shotDirection field on PWA.
        /// CRITICAL: _shotDirection expects a LOCAL direction, not world direction!
        /// We must transform world direction using fireport rotation's inverse.
        /// </summary>
        private static void WriteShotDirection(ulong pwaAddress, Vector3 worldDirection, UnityTransform fireportTransform)
        {
            if (pwaAddress == 0)
            {
                XMLogging.WriteLine("[AIMBOT] WriteShotDirection: PWA address is null!");
                return;
            }
            
            // Get fireport rotation to transform world direction to local direction
            // _shotDirection is in LOCAL space relative to the fireport
            Quaternion? fireportRot = null;
            try
            {
                fireportRot = fireportTransform?.GetRotation();
            }
            catch
            {
                // Failed to get rotation - can't apply silent aim
            }
            
            if (!fireportRot.HasValue)
            {
                if (!_shotDirectionDiagLogged)
                {
                    XMLogging.WriteLine("[AIMBOT] WriteShotDirection: Failed to get fireport rotation!");
                }
                return;
            }
            
            // Convert world direction to LOCAL direction using inverse rotation
            // This is what PWA does - the game expects local space direction
            Vector3 localDirection = InverseTransformDirection(fireportRot.Value, worldDirection);
            
            // Throttle writes
            var now = DateTime.UtcNow.Ticks;
            var msSinceLastWrite = (now - _lastPatchTicks) / TimeSpan.TicksPerMillisecond;
            var directionDelta = (localDirection - _lastPatchedDirection).Length();
            
            if (msSinceLastWrite < PATCH_THROTTLE_MS && directionDelta < 0.01f)
            {
                return; // Skip - too soon and direction hasn't changed much
            }
            
            // Log diagnostic - in DRY RUN mode, log every 500ms for continuous analysis
            var logNow = DateTime.UtcNow.Ticks;
            var msSinceLastLog = (logNow - _lastDryRunLogTicks) / TimeSpan.TicksPerMillisecond;
            bool shouldLog = !_shotDirectionDiagLogged || (SILENT_AIM_DRY_RUN && msSinceLastLog >= 500);
            
            if (shouldLog)
            {
                _shotDirectionDiagLogged = true;
                _lastDryRunLogTicks = logNow;
                
                var targetAddr = pwaAddress + Offsets.ProceduralWeaponAnimation._shotDirection;
                XMLogging.WriteLine("=== DATA-BASED SILENT AIM (PWA METHOD) ===");
                XMLogging.WriteLine($"*** DRY RUN MODE: {SILENT_AIM_DRY_RUN} ***");
                XMLogging.WriteLine($"PWA Address: 0x{pwaAddress:X}");
                XMLogging.WriteLine($"_shotDirection offset: 0x{Offsets.ProceduralWeaponAnimation._shotDirection:X}");
                XMLogging.WriteLine($"Target Address: 0x{targetAddr:X}");
                XMLogging.WriteLine($"World Direction: X={worldDirection.X:F6}, Y={worldDirection.Y:F6}, Z={worldDirection.Z:F6}");
                XMLogging.WriteLine($"Fireport Rotation: X={fireportRot.Value.X:F4}, Y={fireportRot.Value.Y:F4}, Z={fireportRot.Value.Z:F4}, W={fireportRot.Value.W:F4}");
                XMLogging.WriteLine($"LOCAL Direction: X={localDirection.X:F6}, Y={localDirection.Y:F6}, Z={localDirection.Z:F6}");
                XMLogging.WriteLine(SILENT_AIM_DRY_RUN ? "DRY RUN - NOT writing to memory" : "Writing LOCAL Vector3 to _shotDirection");
                XMLogging.WriteLine("=== END DATA-BASED SILENT AIM ===");
            }
            
            // DRY RUN: Skip actual memory write - just log for analysis
            if (SILENT_AIM_DRY_RUN)
            {
                return;
            }
            
            // Write the LOCAL direction vector to _shotDirection field
            Memory.WriteValue(pwaAddress + Offsets.ProceduralWeaponAnimation._shotDirection, localDirection);
            
            _lastPatchTicks = now;
            _lastPatchedDirection = localDirection;
            _shotDirectionNeutral = false; // Mark as non-neutral (aimbot is active)
        }
        
        /// <summary>
        /// Transform a direction from world space to local space using inverse rotation.
        /// This is equivalent to Quaternion.Conjugate(rotation) * direction
        /// </summary>
        private static Vector3 InverseTransformDirection(Quaternion rotation, Vector3 direction)
        {
            // Conjugate of quaternion (inverse for unit quaternions)
            var conjugate = new Quaternion(-rotation.X, -rotation.Y, -rotation.Z, rotation.W);
            return MultiplyQuaternionVector(conjugate, direction);
        }
        
        /// <summary>
        /// Multiply a quaternion by a vector (rotate the vector by the quaternion).
        /// </summary>
        private static Vector3 MultiplyQuaternionVector(Quaternion rotation, Vector3 point)
        {
            float num = rotation.X * 2f;
            float num2 = rotation.Y * 2f;
            float num3 = rotation.Z * 2f;
            float num4 = rotation.X * num;
            float num5 = rotation.Y * num2;
            float num6 = rotation.Z * num3;
            float num7 = rotation.X * num2;
            float num8 = rotation.X * num3;
            float num9 = rotation.Y * num3;
            float num10 = rotation.W * num;
            float num11 = rotation.W * num2;
            float num12 = rotation.W * num3;

            Vector3 result;
            result.X = (1f - (num5 + num6)) * point.X + (num7 - num12) * point.Y + (num8 + num11) * point.Z;
            result.Y = (num7 + num12) * point.X + (1f - (num4 + num6)) * point.Y + (num9 - num10) * point.Z;
            result.Z = (num8 - num11) * point.X + (num9 + num10) * point.Y + (1f - (num4 + num5)) * point.Z;
            return result;
        }

        /// <summary>
        /// Reset the Shot Direction (Silent Aim) back to default state.
        /// PWA approach: Just stop writing - the game will naturally overwrite _shotDirection on next frame.
        /// No need to write a "neutral" value - the game handles it.
        /// </summary>
        private static void ResetSilentAim()
        {
            _shotDirectionDiagLogged = false; // Reset diagnostic flag for next engagement
            _lastDryRunLogTicks = 0; // Reset dry run log timer
            _shotDirectionNeutral = true; // Mark as neutral (we're not writing anymore)
        }

        #endregion

        #region Types
        public enum AimbotTargetingMode : int
        {
            /// <summary>
            /// FOV based targeting.
            /// </summary>
            [Description(nameof(FOV))]
            FOV = 1,
            /// <summary>
            /// CQB (Distance) based targeting.
            /// </summary>
            [Description(nameof(CQB))]
            CQB = 2
        }
        /// <summary>
        /// Encapsulates Aimbot Targeting Results.
        /// </summary>
        private readonly struct PossibleAimbotTarget
        {
            /// <summary>
            /// Target Player that this result belongs to.
            /// </summary>
            public readonly Player Player { get; init; }
            /// <summary>
            /// LocalPlayer's FOV towards this Player.
            /// </summary>
            public readonly float FOV { get; init; }
            /// <summary>
            /// Target's Bone Type.
            /// </summary>
            public readonly Bones Bone { get; init; }
            /// <summary>
            /// LocalPlayer's Distance towards this Player.
            /// </summary>
            public readonly float Distance { get; init; }
        }

        /// <summary>
        /// Cached Values for the AimBot.
        /// Wraps the HandsController Base Address.
        /// </summary>
        public sealed class AimbotCache
        {
            public static implicit operator ulong(AimbotCache x) => x?.HandsBase ?? 0x0;

            /// <summary>
            /// Returns true if Ammo/Ballistics values are valid.
            /// </summary>
            public bool IsAmmoValid => Ballistics.IsAmmoValid;

            /// <summary>
            /// Address for Player.AbstractHandsController.
            /// Will change to a unique value each time a player changes what is in their hands (Weapon/Item/Grenade,etc.)
            /// </summary>
            private ulong HandsBase { get; }
            /// <summary>
            /// EFT.InventoryLogic.Item
            /// </summary>
            public ulong ItemBase { get; }
            /// <summary>
            /// EFT.InventoryLogic.ItemTemplate
            /// </summary>
            public ulong ItemTemplate { get; }
            /// <summary>
            /// Player that is currently 'locked on' to in Phase 1.
            /// </summary>
            public Player AimbotLockedPlayer { get; set; }
            /// <summary>
            /// Ammo Template of the ammo currently in the chamber.
            /// </summary>
            public ulong LoadedAmmo { get; set; }
            /// <summary>
            /// Ballistics Information.
            /// </summary>
            public BallisticsInfo Ballistics { get; } = new();
            /// <summary>
            /// Fireport Transform for LocalPlayer.
            /// </summary>
            public UnityTransform FireportTransform { get; set; }
            /// <summary>
            /// Last position of the Fireport from previous cycle.
            /// Null if first cycle.
            /// </summary>
            public Vector3? LastFireportPos { get; set; }
            /// <summary>
            /// Last position of the Last Player from previous cycle.
            /// Null if first cycle.
            /// </summary>
            public Vector3? LastPlayerPos { get; set; }
            /// <summary>
            /// Last weapon 'version', updates as shots are fired.
            /// </summary>
            public int LastWeaponVersion { get; set; } = -1;
            /// <summary>
            /// Last shot index for detecting when shots are fired.
            /// </summary>
            public sbyte LastShotIndex { get; set; }
            /// <summary>
            /// Last time velocity was logged (for throttling).
            /// </summary>
            public long LastVelocityLogTime { get; set; }
            public Bones? CurrentTargetBone { get; set; }
            public Vector3? CurrentTargetBonePos { get; set; }
            public Vector3? OriginalShotDirection { get; set; }
            public bool?    OriginalShotNeedsFovAdjust { get; set; }   
            /// <param name="handsBase">Player.AbstractHandsController Address</param>
            public AimbotCache(ulong handsBase)
            {
                HandsBase = handsBase;
                ItemBase = Memory.ReadPtr(HandsBase + Offsets.ItemHandsController.Item, false);
                ItemTemplate = Memory.ReadPtr(ItemBase + Offsets.LootItem.Template, false);
                // Initialize shot index to current value to avoid false positive on first check
                LastShotIndex = Memory.ReadValue<sbyte>(HandsBase + Offsets.ClientFirearmController.ShotIndex, false);
            }
            public void CaptureOriginalShotSettings(ulong pwa)
            {
                if (!pwa.IsValidVirtualAddress())
                    return;

                try
                {
                    if (!OriginalShotDirection.HasValue)
                    {
                        OriginalShotDirection = Memory.ReadValue<Vector3>(
                            pwa + Offsets.ProceduralWeaponAnimation._shotDirection,
                            false);
                    }
                }
                catch
                {
                    // ignore, we’ll just fall back later
                }

                try
                {
                    if (!OriginalShotNeedsFovAdjust.HasValue)
                    {
                        OriginalShotNeedsFovAdjust = Memory.ReadValue<bool>(
                            pwa + Offsets.ProceduralWeaponAnimation.ShotNeedsFovAdjustments,
                            false);
                    }
                }
                catch
                {
                    // ignore
                }
            }
            /// <summary>
            /// Reset this Cache to a 'Non-Locked' state.
            /// </summary>
            public void ResetLock()
            {
                LastFireportPos = null;
                LastPlayerPos = null;
                CurrentTargetBone = null;
                CurrentTargetBonePos = null;
            
                if (AimbotLockedPlayer is not null)
                {
                    AimbotLockedPlayer.IsAimbotLocked = false;
                    AimbotLockedPlayer = null;
                }
            }
        }
        #endregion
    }
}