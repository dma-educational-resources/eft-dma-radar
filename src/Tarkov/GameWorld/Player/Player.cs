using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.Tarkov.Features.MemoryWrites.Patches;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Maps;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Config;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Misc.Pools;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.Collections;
using eft_dma_radar.Common.Unity.LowLevel;
using System;
using System.Linq;
using System.Windows.Shapes;
using static System.Windows.Forms.LinkLabel;
using eft_dma_radar.Tarkov.EFTPlayer.SpecialCollections;

namespace eft_dma_radar.Tarkov.EFTPlayer
{
    /// <summary>
    /// Base class for Tarkov Players.
    /// Tarkov implements several distinct classes that implement a similar player interface.
    /// </summary>
    public abstract class Player : IWorldEntity, IMapEntity, IMouseoverEntity, IPlayer, IESPEntity
    {
        #region Group Manager

        /// <summary>
        /// Wrapper Class to manage group allocations.
        /// Thread Safe.
        /// </summary>
        protected sealed class GroupManager
        {
            private readonly Dictionary<string, int> _groups = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Returns the Group Number for a given id.
            /// </summary>
            /// <param name="id">Group ID.</param>
            /// <returns>Group Number (0,1,2,etc.)</returns>
            public int GetGroup(string id)
            {
                lock (_groups)
                {
                    _groups.TryAdd(id, _groups.Count);
                    return _groups[id];
                }
            }

            /// <summary>
            /// Clears the group definitions.
            /// </summary>
            public void Clear()
            {
                lock (_groups)
                {
                    _groups.Clear();
                }
            }
        }

        #endregion

        #region Static Interfaces

        public static implicit operator ulong(Player x) => x.Base;
        private static readonly ConcurrentDictionary<ulong, Stopwatch> _rateLimit = new();
        protected static readonly GroupManager _groups = new();
        protected static int _playerScavNumber = 0;
        public virtual int VoipId { get; }
        /// <summary>
        /// Player History Log.
        /// </summary>
        public static PlayerHistory PlayerHistory { get; } = new();

        /// <summary>
        /// Player Watchlist Entries.
        /// </summary>
        public static PlayerWatchlist PlayerWatchlist { get; } = new();
        /// <summary>
        /// Resets/Updates 'static' assets in preparation for a new game/raid instance.
        /// </summary>
        public static void Reset()
        {
            _groups.Clear();
            _rateLimit.Clear();
            PlayerHistory.Reset();             
            _playerScavNumber = 0;
        }

        #endregion

        #region Allocation

        /// <summary>
        /// Allocates a player and takes into consideration any rate-limits.
        /// </summary>
        /// <param name="playerDict">Player Dictionary collection to add the newly allocated player to.</param>
        /// <param name="playerBase">Player base memory address.</param>
        /// <returns>True if allocation succeeded, false otherwise.</returns>
        public static bool Allocate(ConcurrentDictionary<ulong, Player> playerDict, ulong playerBase)
        {
            var sw = _rateLimit.AddOrUpdate(playerBase,
                key => new Stopwatch(),
                (key, oldValue) => oldValue);
            if (sw.IsRunning && sw.Elapsed.TotalMilliseconds < 500f)
                return false; // Rate limited, not a real failure
            try
            {
                var player = AllocateInternal(playerBase);
                playerDict[player] = player; // Insert or swap
                XMLogging.WriteLine($"Player '{player.Name}' allocated.");
                return true;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"ERROR during Player Allocation for player @ 0x{playerBase:X}: {ex}");
                return false;
            }
            finally
            {
                sw.Restart();
            }
        }

        private static Player AllocateInternal(ulong playerBase)
        {
            if (!ObjectClass.TryReadClassName(playerBase, out var className))
                throw new InvalidOperationException("Player class not ready");

            var isClientPlayer =
                className == "ClientPlayer" ||
                className == "LocalPlayer";

            return isClientPlayer
                ? new ClientPlayer(playerBase)
                : new ObservedPlayer(playerBase);
        }

        /// <summary>
        /// Player Constructor.
        /// </summary>
        protected Player(ulong playerBase)
        {
            ArgumentOutOfRangeException.ThrowIfZero(playerBase, nameof(playerBase));
            Base = playerBase;
        }
        public void SoftResetRuntimeState()
        {
            XMLogging.WriteLine(
                $"[PlayerReset] Soft reset runtime state for '{Name}' @ 0x{Base:X}");

            // Core flags
            IsActive = false;
            IsError = false;
            BtrStickTicks = 0;

            // Position / rotation
            _cachedPosition = Vector3.Zero;
            Rotation = default;

            // Timers / state
            ErrorTimer.Reset();
            HighAlertSw.Reset();

            IsAiming = false;
            IsFocused = false;
            IsVisible = false;

            BoneVisibility.Clear();
            Alerts = null;

            // Skeleton
            try
            {
                Skeleton?.ResetESPCacheAndTransforms();
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine(
                    $"[PlayerReset] Skeleton reset failed for {Name}: {ex}");
            }

            // Gear / hands
            Gear = null;
            Hands = null;
        }

        #endregion

        #region Fields / Properties
        const float HEIGHT_INDICATOR_THRESHOLD = 1.85f;
        const float HEIGHT_INDICATOR_ARROW_SIZE = 2f;
        /// <summary>
        /// Linecast visibility info.
        /// </summary>
        public bool[] VisibilityInfo { get; set; }
        public int ListIndex { get; set; }
        public bool IsVisible { get; set; } = false;
        public Dictionary<Bones, bool> BoneVisibility { get; } = new();
        public static readonly List<(Bones start, Bones end)> BoneSegments = new List<(Bones, Bones)>
        {
            (Bones.HumanHead, Bones.HumanNeck),
            (Bones.HumanNeck, Bones.HumanSpine3),
            (Bones.HumanSpine3, Bones.HumanSpine2),
            (Bones.HumanSpine2, Bones.HumanSpine1),
            (Bones.HumanSpine1, Bones.HumanPelvis),

            (Bones.HumanPelvis, Bones.HumanLThigh2),   // left knee
            (Bones.HumanLThigh2, Bones.HumanLFoot),    // left foot

            (Bones.HumanPelvis, Bones.HumanRThigh2),   // right knee
            (Bones.HumanRThigh2, Bones.HumanRFoot),    // right foot

            (Bones.HumanLCollarbone, Bones.HumanLForearm2),  // left elbow
            (Bones.HumanLForearm2, Bones.HumanLPalm),         // left hand

            (Bones.HumanRCollarbone, Bones.HumanRForearm2),  // right elbow
            (Bones.HumanRForearm2, Bones.HumanRPalm),         // right hand
        };        
        /// <summary>
        /// Player Class Base Address
        /// </summary>
        public ulong Base { get; }

        /// <summary>
        /// True if the Player is Active (in the player list).
        /// </summary>
        public bool IsActive { get; private set; }
        
        /// <summary>
        /// TRUE if critical memory reads (position/rotation) have failed.
        /// </summary>
        public bool IsError { get; set; }

        /// <summary>
        /// Type of player unit.
        /// </summary>
        public PlayerType Type { get; protected set; }

        /// <summary>
        /// Streaming platform username.
        /// </summary>
        public string StreamingUsername { get; set; }

        /// <summary>
        /// The streaming platform URL they're streaming
        /// </summary>
        public string StreamingURL { get; set; }

        /// <summary>
        /// Player's Rotation in Local Game World.
        /// </summary>
        public Vector2 Rotation { get; private set; }

        /// <summary>
        /// Player's Map Rotation (with 90 degree correction applied).
        /// </summary>
        public float MapRotation
        {
            get
            {
                float mapRotation = Rotation.X; // Cache value
                mapRotation -= 90f;
                while (mapRotation < 0f)
                    mapRotation += 360f;

                return mapRotation;
            }
        }

        /// <summary>
        /// Corpse field value.
        /// </summary>
        public ulong? Corpse { get; private set; }

        /// <summary>
        /// Stopwatch for High Alert ESP Feature.
        /// </summary>
        public Stopwatch HighAlertSw { get; } = new();

        /// <summary>
        /// Player's Skeleton Bones.
        /// Derived types MUST define this.
        /// </summary>
        public virtual Skeleton Skeleton { get; protected set; }

        /// <summary>
        /// Duration of consecutive errors.
        /// </summary>
        public Stopwatch ErrorTimer { get; } = new();
        
        /// <summary>
        /// Cached position fallback to prevent players from disappearing when skeleton temporarily fails.
        /// </summary>
        protected Vector3 _cachedPosition;
        
        /// <summary>
        /// Dynamic vertex count for skeleton reads (recalculated each frame if needed).
        /// </summary>
        protected int _verticesCount;
        
        /// <summary>
        /// Flag to prevent skeleton error log spam.
        /// </summary>
        private bool _skeletonErrorLogged;

        /// <summary>
        /// Player's Gear/Loadout Information and contained items.
        /// </summary>
        public GearManager Gear { get; private set; }

        /// <summary>
        /// Contains information about the item/weapons in Player's hands.
        /// </summary>
        public HandsManager Hands { get; private set; }

        /// <summary>
        /// True if player is 'Locked On' via Aimbot.
        /// </summary>
        public bool IsAimbotLocked
        {
            get => _isAimbotLocked;
            set
            {
                if (_isAimbotLocked != value)
                {
                    _isAimbotLocked = value;
                }
            }
        }

        /// <summary>
        /// True if player is being focused via Right-Click (UI).
        /// </summary>
        public bool IsFocused { get; set; }

        /// <summary>
        /// Dead Player's associated loot container object.
        /// </summary>
        public LootContainer LootObject { get; set; }

        /// <summary>
        /// True if the player is streaming
        /// </summary>
        public bool IsStreaming { get; set; }

        /// <summary>
        /// Alerts for this Player Object.
        /// Used by Player History UI Interop.
        /// </summary>
        public string Alerts { get; private set; }

        public Vector2 MouseoverPosition { get; set; }
        public bool IsAiming { get; set; } = false;
        private bool _isAimbotLocked;
        internal float LastBtrMapRotation;
        internal int BtrStaticRotationTicks;
        #endregion

        #region Virtual Properties

        /// <summary>
        /// Player name.
        /// </summary>
        public virtual string Name { get; set; }

        /// <summary>
        /// Account UUID for Human Controlled Players.
        /// </summary>
        public virtual string AccountID { get; set; }

        /// <summary>
        /// Group that the player belongs to.
        /// </summary>
        public virtual int GroupID { get; } = -1;
        public virtual int NetworkGroupID { get; } = -1;
        public virtual int SpawnGroupID { get; } = -1;
        public virtual string ProfileID { get; set; }

        /// <summary>
        /// Player's Faction.
        /// </summary>
        public virtual Enums.EPlayerSide PlayerSide { get; }

        /// <summary>
        /// Player is Human-Controlled.
        /// </summary>
        public virtual bool IsHuman { get; }

        /// <summary>
        /// MovementContext / StateContext
        /// </summary>
        public virtual ulong MovementContext { get; }

        /// <summary>
        /// EFT.PlayerBody
        /// </summary>
        public virtual ulong Body { get; }

        /// <summary>
        /// Inventory Controller field address.
        /// </summary>
        public virtual ulong InventoryControllerAddr { get; }

        /// <summary>
        /// Hands Controller field address.
        /// </summary>
        public virtual ulong HandsControllerAddr { get; }

        /// <summary>
        /// Corpse field address..
        /// </summary>
        public virtual ulong CorpseAddr { get; }

        /// <summary>
        /// Player Rotation Field Address (view angles).
        /// </summary>
        public virtual ulong RotationAddress { get; }
        public virtual float ZoomLevel { get; set; }
        public virtual ulong PWA { get; set; }

        public virtual ref Vector3 Position
        {
            get
            {
                // HARD GUARD ¡ª prevents ALL render crashes
                if (Skeleton == null || Skeleton.Root == null)
                    return ref _cachedPosition;

                var pos = Skeleton.Root.Position;

                if (pos.IsFinite() && pos != Vector3.Zero)
                {
                    _cachedPosition = pos;
                    return ref Skeleton.Root.Position;
                }

                return ref _cachedPosition;
            }
        }


        #endregion

        #region Boolean Getters

        /// <summary>
        /// Player is AI-Controlled.
        /// </summary>
        public bool IsAI => !IsHuman;

        /// <summary>
        /// Player is a PMC Operator.
        /// </summary>
        public bool IsPmc => PlayerSide is Enums.EPlayerSide.Usec || PlayerSide is Enums.EPlayerSide.Bear;

        /// <summary>
        /// Player is a SCAV.
        /// </summary>
        public bool IsScav => PlayerSide is Enums.EPlayerSide.Savage;

        /// <summary>
        /// Player is alive (not dead).
        /// </summary>
        public bool IsAlive => Corpse is null;

        /// <summary>
        /// True if Player is Friendly to LocalPlayer.
        /// </summary>
        public bool IsFriendly => this is LocalPlayer || Type is PlayerType.Teammate;

        /// <summary>
        /// True if player is Hostile to LocalPlayer.
        /// </summary>
        public bool IsHostile => !IsFriendly;

        /// <summary>
        /// Player is Alive/Active and NOT LocalPlayer.
        /// </summary>
        public bool IsNotLocalPlayerAlive => this is not LocalPlayer && IsActive && IsAlive;

        /// <summary>
        /// Player is a Hostile PMC Operator.
        /// </summary>
        public bool IsHostilePmc => IsPmc && IsHostile;

        /// <summary>
        /// Player is human-controlled (Not LocalPlayer).
        /// </summary>
        public bool IsHumanOther => IsHuman && this is not LocalPlayer;

        /// <summary>
        /// Player is AI Controlled and Alive/Active.
        /// </summary>
        public bool IsAIActive => IsAI && IsActive && IsAlive;

        /// <summary>
        /// Player is AI Controlled and Alive/Active & their AI Role is default.
        /// </summary>
        public bool IsDefaultAIActive => IsAI && Name == "defaultAI" && IsActive && IsAlive;

        /// <summary>
        /// Player is human-controlled and Active/Alive.
        /// </summary>
        public bool IsHumanActive => IsHuman && IsActive && IsAlive;

        /// <summary>
        /// Player is hostile and alive/active.
        /// </summary>
        public bool IsHostileActive => IsHostile && IsActive && IsAlive;

        /// <summary>
        /// Player is human-controlled & Hostile.
        /// </summary>
        public bool IsHumanHostile => IsHuman && IsHostile;

        /// <summary>
        /// Player is human-controlled, hostile, and Active/Alive.
        /// </summary>
        public bool IsHumanHostileActive => IsHumanHostile && IsActive && IsAlive;

        /// <summary>
        /// Player is friendly to LocalPlayer (including LocalPlayer) and Active/Alive.
        /// </summary>
        public bool IsFriendlyActive => IsFriendly && IsActive && IsAlive;

        /// <summary>
        /// Player has exfil'd/left the raid.
        /// </summary>
        public bool HasExfild => !IsActive && IsAlive;

        private static Config Config => Program.Config;

        private bool BattleMode => Config.BattleMode;

        #endregion

        #region Methods

        private readonly Lock _alertsLock = new();
        /// <summary>
        /// Update the Alerts for this Player Object.
        /// </summary>
        /// <param name="alert">Alert to set.</param>
        public void UpdateAlerts(string alert)
        {
            if (alert is null)
                return;

            lock (_alertsLock)
            {
                if (this.Alerts is null)
                    this.Alerts = alert;
                else
                    this.Alerts = $"{alert} | {this.Alerts}";
            }
        }

        public void ClearAlerts()
        {
            lock (_alertsLock)
            {
                this.Alerts = null;
            }
        }

        public void UpdatePlayerType(PlayerType newType)
        {
            this.Type = newType;
        }

        public void UpdateStreamingUsername(string url)
        {
            this.StreamingUsername = url;
        }

        /// <summary>
        /// Validates the Rotation Address.
        /// </summary>
        /// <param name="rotationAddr">Rotation va</param>
        /// <returns>Validated rotation virtual address.</returns>
        protected static ulong ValidateRotationAddr(ulong rotationAddr)
        {
            var rotation = Memory.ReadValue<Vector2>(rotationAddr, false);
            if (!rotation.IsNormalOrZero() ||
                Math.Abs(rotation.X) > 360f ||
                Math.Abs(rotation.Y) > 90f)
                throw new ArgumentOutOfRangeException(nameof(rotationAddr));

            return rotationAddr;
        }

        /// <summary>
        /// Refreshes non-realtime player information. Call in the Registered Players Loop (T0).
        /// </summary>
        /// <param name="index"></param>
        /// <param name="registered"></param>
        /// <param name="isActiveParam"></param>
        public virtual void OnRegRefresh(ScatterReadIndex index, IReadOnlySet<ulong> registered, bool? isActiveParam = null)
        {
            if (!this.TryInitSkeleton())
                return; 

            if (this is ObservedPlayer op &&
                op.IsPmc &&
                op.IsHuman &&
                op.VoipId > 0)
            {
                PlayerListWorker.GetOrAssignDisplayName(op);
            }            
            if (isActiveParam is not bool isActive)
                isActive = registered.Contains(this);
            if (isActive)
            {
                this.SetAlive();
            }
            else if (this.IsAlive) // Not in list, but alive
            {
                index.AddEntry<ulong>(0, this.CorpseAddr);
                index.Callbacks += x1 =>
                {
                    if (x1.TryGetResult<ulong>(0, out var corpsePtr) && corpsePtr != 0x0)
                        this.SetDead(corpsePtr);
                    else
                        this.SetExfild();
                };
            }
        }

        /// <summary>
        /// Mark player as dead.
        /// </summary>
        /// <param name="corpse">Corpse address.</param>
        public void SetDead(ulong corpse)
        {
            Corpse = corpse;
            IsActive = false;
        }

        /// <summary>
        /// Mark player as exfil'd.
        /// </summary>
        private void SetExfild()
        {
            Corpse = null;
            IsActive = false;
        }

        /// <summary>
        /// Mark player as alive.
        /// </summary>
        private void SetAlive()
        {
            Corpse = null;
            LootObject = null;
            IsActive = true;
        }
        private ulong _lastMovementPlayer;
        internal int BtrStickTicks;
        /// <summary>
        /// Executed on each Realtime Loop.
        /// </summary>
        /// <param name="index">Scatter read index dedicated to this player.</param>
        public virtual void OnRealtimeLoop(ScatterReadIndex index)
        {
            // Ensure skeleton exists
            if (!this.TryInitSkeleton())
                return;

            // -------------------------
            // Scatter read setup
            // -------------------------

            // Rotation
            index.AddEntry<Vector2>(-1, this.RotationAddress);

            // Bone vertices
            foreach (var tr in Skeleton.Bones)
            {
                index.AddEntry<SharedArray<UnityTransform.TrsX>>(
                    (int)(uint)tr.Key,
                    tr.Value.VerticesAddr,
                    (3 * tr.Value.Index + 3) * 16);
            }

            // -------------------------
            // Scatter callback
            // -------------------------
            index.Callbacks += x1 =>
            {
                bool rotationOk = false;
                bool bonesOk = true;

                // ---- Rotation ----
                if (x1.TryGetResult<Vector2>(-1, out var rotation))
                    rotationOk = this.SetRotation(ref rotation);

                // ---- Bones ----
                foreach (var tr in Skeleton.Bones)
                {
                    if (x1.TryGetResult<SharedArray<UnityTransform.TrsX>>(
                            (int)(uint)tr.Key,
                            out var vertices))
                    {
                        try
                        {
                            tr.Value.UpdatePosition(vertices);
                        }
                        catch
                        {
                            // Transform chain likely invalidated ¡ú rebuild just this bone
                            Skeleton.ResetTransform(tr.Key);
                            bonesOk = false;
                        }
                    }
                    else
                    {
                        bonesOk = false;
                    }
                }

                // -------------------------
                // Stuck detection (AFTER update)
                // -------------------------
                Skeleton.UpdateStuckDetection();

                // -------------------------
                // Error / health tracking
                // -------------------------
                if (rotationOk && bonesOk)
                    ErrorTimer.Reset();
                else
                    ErrorTimer.Start();

                // -------------------------
                // Stuck recovery (ONE-SHOT)
                // -------------------------
                if (Skeleton.IsLikelyStuck &&
                    ErrorTimer.ElapsedMilliseconds > 800)
                {
                    XMLogging.WriteLine(
                        $"[SKELETON FIX] {Name} skeleton frozen ¡ú soft reset");

                    SoftResetRuntimeState();
                    Skeleton.ResetESPCacheAndTransforms();

                    // Explicitly clear stuck state
                    Skeleton.IsLikelyStuck = false;
                }
            };
        }

        protected bool TryInitSkeleton()
        {
            if (Skeleton != null)
                return true;

            try
            {
                if (Body == 0 || !Body.IsValidVirtualAddress())
                    return false;

                Skeleton = new Skeleton(this, GetTransformInternalChain);
                return true;
            }
            catch
            {
                Skeleton = null;
                return false;
            }
        }
        /// <summary>
        /// Executed on each Transform Validation Loop.
        /// </summary>
        /// <param name="round1">Index (round 1)</param>
        /// <param name="round2">Index (round 2)</param>
        public void OnValidateTransforms(ScatterReadIndex round1, ScatterReadIndex round2)
        {
            // Check skeleton root transform first (most critical - if this changes, rebuild everything)
            round1.AddEntry<MemPointer>(-1, Skeleton.Root.TransformInternal +
                UnityOffsets.TransformInternal.TransformAccess); // Root Hierarchy
            round1.Callbacks += x1 =>
            {
                if (x1.TryGetResult<MemPointer>(-1, out var tra))
                {
                    round2.AddEntry<MemPointer>(-1, tra + UnityOffsets.TransformAccess.Vertices); // Root Vertices Ptr
                    round2.Callbacks += x2 =>
                    {
                        if (x2.TryGetResult<MemPointer>(-1, out var verticesPtr))
                        {
                            if (Skeleton.Root.VerticesAddr != verticesPtr) // Root vertices address changed
                            {
                                // Rebuild root transform
                                var transform = new UnityTransform(Skeleton.Root.TransformInternal);
                                // Note: We can't directly set Skeleton.Root, but we can reset all bones
                                // The root will be updated when we reset the HumanBase bone
                                _verticesCount = 0; // Force fresh vertex count on next read

                                // IMPORTANT: Rebuild all bone transforms when root changes
                                try
                                {
                                    foreach (var bone in Skeleton.Bones.Keys.ToList())
                                    {
                                        Skeleton.ResetTransform(bone);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    XMLogging.WriteLine($"ERROR rebuilding skeleton for '{Name}': {ex}");
                                }
                            }
                        }
                    };
                }
            };

            // Also check individual bone transforms (for non-root changes)
            foreach (var tr in Skeleton.Bones)
            {
                // Skip root bone (already checked above)
                if (tr.Key == eft_dma_radar.Common.Unity.Bones.HumanBase)
                    continue;

                round1.AddEntry<MemPointer>((int)(uint)tr.Key,
                    tr.Value.TransformInternal +
                    UnityOffsets.TransformInternal.TransformAccess); // Bone Hierarchy
                round1.Callbacks += x1 =>
                {
                    if (x1.TryGetResult<MemPointer>((int)(uint)tr.Key, out var tra))
                        round2.AddEntry<MemPointer>((int)(uint)tr.Key, tra + UnityOffsets.TransformAccess.Vertices); // Vertices Ptr
                    round2.Callbacks += x2 =>
                    {
                        if (x2.TryGetResult<MemPointer>((int)(uint)tr.Key, out var verticesPtr))
                        {
                            if (tr.Value.VerticesAddr != verticesPtr) // check if any addr changed
                            {
                                this.Skeleton.ResetTransform(tr.Key); // alloc new transform
                            }
                        }
                    };
                };
            }
        }

        /// <summary>
        /// Set player rotation (Direction/Pitch)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual bool SetRotation(ref Vector2 rotation)
        {
            try
            {
                rotation.ThrowIfAbnormalAndNotZero();
                rotation.X = rotation.X.NormalizeAngle();
                ArgumentOutOfRangeException.ThrowIfLessThan(rotation.X, 0f);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(rotation.X, 360f);
                ArgumentOutOfRangeException.ThrowIfLessThan(rotation.Y, -90f);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(rotation.Y, 90f);
                Rotation = rotation;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Refresh Gear if Active Human Player.
        /// </summary>
        public void RefreshGear()
        {
            try
            {
                Gear ??= new GearManager(this, IsPmc);
                Gear?.Refresh();
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[GearManager] ERROR for Player {Name}: {ex}");
            }
        }

        /// <summary>
        /// Refresh item in player's hands.
        /// </summary>
        public void RefreshHands()
        {
            try
            {
                if (IsActive && IsAlive)
                {
                    Hands ??= new HandsManager(this);
                    Hands?.Refresh();
                }
            }
            catch { }
        }

        /// <summary>
        /// Get the Transform Internal Chain for this Player.
        /// </summary>
        /// <param name="bone">Bone to lookup.</param>
        /// <returns>Array of offsets for transform internal chain.</returns>
        public virtual uint[] GetTransformInternalChain(Bones bone) =>
            throw new NotImplementedException();

        #endregion

        #region AI Player Types

        public readonly struct AIRole
        {
            public readonly string Name { get; init; }
            public readonly PlayerType Type { get; init; }
        }

        /// <summary>
        /// Lookup AI Info based on Voice Line.
        /// </summary>
        /// <param name="voiceLine"></param>
        /// <returns></returns>
        public static AIRole GetAIRoleInfo(string voiceLine)
        {
            switch (voiceLine)
            {
                case "BossSanitar":
                    return new AIRole()
                    {
                        Name = "Sanitar",
                        Type = PlayerType.AIBoss
                    };
                case "BossBully":
                    return new AIRole()
                    {
                        Name = "Reshala",
                        Type = PlayerType.AIBoss
                    };
                case "BossGluhar":
                    return new AIRole()
                    {
                        Name = "Gluhar",
                        Type = PlayerType.AIBoss
                    };
                case "SectantPriest":
                    return new AIRole()
                    {
                        Name = "Priest",
                        Type = PlayerType.AIBoss
                    };
                case "SectantWarrior":
                    return new AIRole()
                    {
                        Name = "Cultist",
                        Type = PlayerType.AIRaider
                    };
                case "BossKilla":
                    return new AIRole()
                    {
                        Name = "Killa",
                        Type = PlayerType.AIBoss
                    };
                case "BossTagilla":
                    return new AIRole()
                    {
                        Name = "Tagilla",
                        Type = PlayerType.AIBoss
                    };
                case "Boss_Partizan":
                    return new AIRole()
                    {
                        Name = "Partisan",
                        Type = PlayerType.AIBoss
                    };
                case "BossBigPipe":
                    return new AIRole()
                    {
                        Name = "Big Pipe",
                        Type = PlayerType.AIBoss
                    };
                case "BossBirdEye":
                    return new AIRole()
                    {
                        Name = "Birdeye",
                        Type = PlayerType.AIBoss
                    };
                case "BossKnight":
                    return new AIRole()
                    {
                        Name = "Knight",
                        Type = PlayerType.AIBoss
                    };
                case "Arena_Guard_1":
                    return new AIRole()
                    {
                        Name = "Arena Guard",
                        Type = PlayerType.AIScav
                    };
                case "Arena_Guard_2":
                    return new AIRole()
                    {
                        Name = "Arena Guard",
                        Type = PlayerType.AIScav
                    };
                case "Boss_Kaban":
                    return new AIRole()
                    {
                        Name = "Kaban",
                        Type = PlayerType.AIBoss
                    };
                case "Boss_Kollontay":
                    return new AIRole()
                    {
                        Name = "Kollontay",
                        Type = PlayerType.AIBoss
                    };
                case "Boss_Sturman":
                    return new AIRole()
                    {
                        Name = "Shturman",
                        Type = PlayerType.AIBoss
                    };
                case "Zombie_Generic":
                    return new AIRole()
                    {
                        Name = "Zombie",
                        Type = PlayerType.AIScav
                    };
                case "BossZombieTagilla":
                    return new AIRole()
                    {
                        Name = "Zombie Tagilla",
                        Type = PlayerType.AIBoss
                    };
                case "Zombie_Fast":
                    return new AIRole()
                    {
                        Name = "Zombie",
                        Type = PlayerType.AIScav
                    };
                case "Zombie_Medium":
                    return new AIRole()
                    {
                        Name = "Zombie",
                        Type = PlayerType.AIScav
                    };
                // Rogue (ex-USEC) voice lines
                case "VSRF_01":
                case "VSRF_02":
                case "VSRF_03":
                    return new AIRole()
                    {
                        Name = "Rogue",
                        Type = PlayerType.AIRaider
                    };
                default:
                    break;
            }
            if (voiceLine.Contains("scav", StringComparison.OrdinalIgnoreCase))
                return new AIRole()
                {
                    Name = "Scav",
                    Type = PlayerType.AIScav
                };
            if (voiceLine.Contains("boss", StringComparison.OrdinalIgnoreCase))
                return new AIRole()
                {
                    Name = "Boss",
                    Type = PlayerType.AIBoss
                };
            if (voiceLine.Contains("usec", StringComparison.OrdinalIgnoreCase))
                return new AIRole()
                {
                    Name = "Usec",
                    Type = PlayerType.AIScav
                };
            if (voiceLine.Contains("bear", StringComparison.OrdinalIgnoreCase))
                return new AIRole()
                {
                    Name = "Bear",
                    Type = PlayerType.AIScav
                };
            XMLogging.WriteLine($"Unknown Voice Line: {voiceLine}");
            return new AIRole()
            {
                Name = "AI",
                Type = PlayerType.AIScav
            };
        }

        public static AIRole GetAIRoleInfo(Enums.WildSpawnType wildSpawnType)
        {
            switch (wildSpawnType)
            {
                case Enums.WildSpawnType.marksman:
                    return new AIRole()
                    {
                        Name = "Sniper",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.assault:
                    return new AIRole()
                    {
                        Name = "Scav",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.bossTest:
                    return new AIRole()
                    {
                        Name = "bossTest",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.bossBully:
                    return new AIRole()
                    {
                        Name = "Reshala",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.followerTest:
                    return new AIRole()
                    {
                        Name = "followerTest",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.followerBully:
                    return new AIRole()
                    {
                        Name = "Guard",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.bossKilla:
                    return new AIRole()
                    {
                        Name = "Killa",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.bossKojaniy:
                    return new AIRole()
                    {
                        Name = "Shturman",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.followerKojaniy:
                    return new AIRole()
                    {
                        Name = "Guard",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.pmcBot:
                    return new AIRole()
                    {
                        Name = "Raider",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.cursedAssault:
                    return new AIRole()
                    {
                        Name = "Scav",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.bossGluhar:
                    return new AIRole()
                    {
                        Name = "Gluhar",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.followerGluharAssault:
                    return new AIRole()
                    {
                        Name = "Assault",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.followerGluharSecurity:
                    return new AIRole()
                    {
                        Name = "Security",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.followerGluharScout:
                    return new AIRole()
                    {
                        Name = "Scout",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.followerGluharSnipe:
                    return new AIRole()
                    {
                        Name = "Sniper",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.followerSanitar:
                    return new AIRole()
                    {
                        Name = "Guard",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.bossSanitar:
                    return new AIRole()
                    {
                        Name = "Sanitar",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.test:
                    return new AIRole()
                    {
                        Name = "test",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.assaultGroup:
                    return new AIRole()
                    {
                        Name = "Scav",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.sectantWarrior:
                    return new AIRole()
                    {
                        Name = "Cultist",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.sectantPriest:
                    return new AIRole()
                    {
                        Name = "Priest",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.bossTagilla:
                    return new AIRole()
                    {
                        Name = "Tagilla",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.followerTagilla:
                    return new AIRole()
                    {
                        Name = "Tagilla",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.exUsec:
                    return new AIRole()
                    {
                        Name = "Rogue",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.gifter:
                    return new AIRole()
                    {
                        Name = "Santa",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.bossKnight:
                    return new AIRole()
                    {
                        Name = "Knight",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.followerBigPipe:
                    return new AIRole()
                    {
                        Name = "Big Pipe",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.followerBirdEye:
                    return new AIRole()
                    {
                        Name = "Bird Eye",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.bossZryachiy:
                    return new AIRole()
                    {
                        Name = "Zryachiy",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.followerZryachiy:
                    return new AIRole()
                    {
                        Name = "Cultist",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.bossBoar:
                    return new AIRole()
                    {
                        Name = "Kaban",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.followerBoar:
                    return new AIRole()
                    {
                        Name = "Guard",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.arenaFighter:
                    return new AIRole()
                    {
                        Name = "Arena Fighter",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.arenaFighterEvent:
                    return new AIRole()
                    {
                        Name = "Bloodhound",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.bossBoarSniper:
                    return new AIRole()
                    {
                        Name = "Guard",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.crazyAssaultEvent:
                    return new AIRole()
                    {
                        Name = "Scav",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.peacefullZryachiyEvent:
                    return new AIRole()
                    {
                        Name = "peacefullZryachiyEvent",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.sectactPriestEvent:
                    return new AIRole()
                    {
                        Name = "sectactPriestEvent",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.ravangeZryachiyEvent:
                    return new AIRole()
                    {
                        Name = "ravangeZryachiyEvent",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.followerBoarClose1:
                    return new AIRole()
                    {
                        Name = "Guard",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.followerBoarClose2:
                    return new AIRole()
                    {
                        Name = "Guard",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.bossKolontay:
                    return new AIRole()
                    {
                        Name = "Kolontay",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.followerKolontayAssault:
                    return new AIRole()
                    {
                        Name = "Guard",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.followerKolontaySecurity:
                    return new AIRole()
                    {
                        Name = "Guard",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.shooterBTR:
                    return new AIRole()
                    {
                        Name = "BTR",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.bossPartisan:
                    return new AIRole()
                    {
                        Name = "Partisan",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.spiritWinter:
                    return new AIRole()
                    {
                        Name = "spiritWinter",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.spiritSpring:
                    return new AIRole()
                    {
                        Name = "spiritSpring",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.peacemaker:
                    return new AIRole()
                    {
                        Name = "Peacekeeper Goon",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.pmcBEAR:
                    return new AIRole()
                    {
                        Name = "BEAR",
                        Type = PlayerType.BEAR
                    };
                case Enums.WildSpawnType.pmcUSEC:
                    return new AIRole()
                    {
                        Name = "USEC",
                        Type = PlayerType.USEC
                    };
                case Enums.WildSpawnType.skier:
                    return new AIRole()
                    {
                        Name = "Skier Goon",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.sectantPredvestnik:
                    return new AIRole()
                    {
                        Name = "Partisan",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.sectantPrizrak:
                    return new AIRole()
                    {
                        Name = "Ghost",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.sectantOni:
                    return new AIRole()
                    {
                        Name = "Oni",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.infectedAssault:
                    return new AIRole()
                    {
                        Name = "Zombie",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.infectedPmc:
                    return new AIRole()
                    {
                        Name = "Zombie",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.infectedCivil:
                    return new AIRole()
                    {
                        Name = "Zombie",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.infectedLaborant:
                    return new AIRole()
                    {
                        Name = "Zombie",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.infectedTagilla:
                    return new AIRole()
                    {
                        Name = "Zombie Tagilla",
                        Type = PlayerType.AIBoss
                    };
                default:
                    XMLogging.WriteLine("WARNING: Unknown WildSpawnType: " + (int)wildSpawnType);
                    return new AIRole()
                    {
                        Name = "defaultAI",
                        Type = PlayerType.AIScav
                    };
            }
        }

        #endregion

        #region Interfaces

        public void Draw(SKCanvas canvas, XMMapParams mapParams, ILocalPlayer localPlayer)
        {
            try
            {
                if (!Skeleton.HasValidPosition)
                    return;

                // PERF: cache once
                var playerTypeKey = DeterminePlayerTypeKey();
                var typeSettings  = Config.PlayerTypeSettings.GetSettings(playerTypeKey);

                var dist = Vector3.Distance(localPlayer.Position, Position);
                if (dist > typeSettings.RenderDistance)
                    return;

                var mapPos = Position.ToMapPos(mapParams.Map);
                var point  = mapPos.ToZoomedPos(mapParams);
                MouseoverPosition = new Vector2(point.X, point.Y);

                if (!IsAlive)
                {
                    if (Config.ShowCorpseMarkers)
                    {
                        var corpseColor = GetCorpseFilterColor();
                        DrawDeathMarker(canvas, point, corpseColor);
                    }
                    return;
                }

                DrawPlayerMarker(canvas, localPlayer, point, typeSettings);

                if (this == localPlayer || BattleMode)
                    return;

                var height = Position.Y - localPlayer.Position.Y;

                string nameText     = null;
                string distanceText = null;
                string heightText   = null;

                // PERF: reuse list instead of recreating many temporaries
                var rightSideInfo = new List<string>(8);

                // PERF: snapshot loot once
                var gearLoot = Gear?.Loot;
                var hasImportantItems =
                    Type != PlayerType.Teammate &&
                    (gearLoot != null &&
                     (gearLoot.Any(i => i.IsImportant) ||
                      (Config.QuestHelper.Enabled && Gear.HasQuestItems)));

                if (typeSettings.ShowName)
                {
                    var name = ErrorTimer.ElapsedMilliseconds > 100
                        ? "ERROR"
                        : (Config.MaskNames && IsHuman ? "<Hidden>" : Name);

                    nameText = name;
                }

                if (typeSettings.ShowDistance)
                    distanceText = ((int)dist).ToString();

                // PERF: snapshot + filtered list once
                List<LootItem> importantLootItems = null;

                if (typeSettings.ShowImportantLoot &&
                    IsAlive &&
                    gearLoot != null &&
                    Type != PlayerType.Teammate)
                {
                    var snapshot = gearLoot.ToList();
                    importantLootItems = snapshot
                        .Where(item =>
                               item.IsImportant ||
                               item is QuestItem ||
                               (Config.QuestHelper.Enabled && item.IsQuestCondition) ||
                               item.IsWishlisted ||
                               (LootFilterControl.ShowBackpacks && item.IsBackpack) ||
                               (LootFilterControl.ShowMeds && item.IsMeds) ||
                               (LootFilterControl.ShowFood && item.IsFood) ||
                               (LootFilterControl.ShowWeapons && item.IsWeapon) ||
                               item.IsValuableLoot ||
                               (!item.IsGroupedBlacklisted &&
                                item.MatchedFilter?.Color != null &&
                                !string.IsNullOrEmpty(item.MatchedFilter.Color)))
                        .OrderLoot()
                        .Take(5)
                        .ToList();
                }

                if (typeSettings.ShowHeight && !typeSettings.HeightIndicator)
                    heightText = ((int)height).ToString();

                if (this is ObservedPlayer observed)
                {
                    if (typeSettings.ShowHealth &&
                        observed.HealthStatus != Enums.ETagStatus.Healthy)
                        rightSideInfo.Add(observed.HealthStatus.GetDescription());

                    if (typeSettings.ShowLevel &&
                        observed.Profile?.Level is int lvl)
                        rightSideInfo.Add($"L:{lvl}");

                    if (typeSettings.ShowKD &&
                        observed.Profile?.Overall_KD is float kd &&
                        kd >= typeSettings.MinKD)
                        rightSideInfo.Add(kd.ToString("n2"));
                }

                if (this is ObservedPlayer op && typeSettings.ShowGroupID)
                {
                    if (op.SpawnGroupID != -1)
                        rightSideInfo.Add($"SG:{op.SpawnGroupID}");
                    else if (op.NetworkGroupID != -1)
                        rightSideInfo.Add($"NG:{op.NetworkGroupID}");
                }

                if (typeSettings.ShowADS && IsAiming) rightSideInfo.Add("ADS");
                if (typeSettings.ShowWeapon && Hands?.CurrentItem != null) rightSideInfo.Add(Hands.CurrentItem);
                if (typeSettings.ShowAmmoType && Hands?.CurrentAmmo != null) rightSideInfo.Add(Hands.CurrentAmmo);
                if (typeSettings.ShowThermal && Gear?.HasThermal == true) rightSideInfo.Add("THERMAL");
                if (typeSettings.ShowNVG && Gear?.HasNVG == true) rightSideInfo.Add("NVG");
                if (typeSettings.ShowUBGL && Gear?.HasUBGL == true) rightSideInfo.Add("UBGL");
                if (typeSettings.ShowValue && Gear?.Value > 0) rightSideInfo.Add(TarkovMarketItem.FormatPrice(Gear.Value));
                if (typeSettings.ShowTag && !string.IsNullOrEmpty(Alerts)) rightSideInfo.Add(Alerts);

                DrawPlayerText(
                    canvas,
                    point,
                    nameText,
                    distanceText,
                    heightText,
                    rightSideInfo,
                    hasImportantItems,
                    importantLootItems
                );

                if (typeSettings.ShowHeight && typeSettings.HeightIndicator)
                    DrawAlternateHeightIndicator(canvas, point, height, GetPaints());
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"WARNING! Player Draw Error: {ex}");
            }
        }

        private void DrawAlternateHeightIndicator(SKCanvas canvas, SKPoint point, float heightDiff, ValueTuple<SKPaint, SKPaint> paints)
        { 
            var baseX = point.X - (15.0f * MainWindow.UIScale);
            var baseY = point.Y + (3.5f * MainWindow.UIScale);

            SKPaints.ShapeOutline.StrokeWidth = 2f * MainWindow.UIScale;

            var arrowSize = HEIGHT_INDICATOR_ARROW_SIZE * MainWindow.UIScale;
            var circleSize = arrowSize * 0.7f;

            if (heightDiff > HEIGHT_INDICATOR_THRESHOLD)
            {
                var upArrowPoint = new SKPoint(baseX, baseY - arrowSize);
                using var path = upArrowPoint.GetUpArrow(arrowSize);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paints.Item1);
            }
            else if (heightDiff < -HEIGHT_INDICATOR_THRESHOLD)
            {
                var downArrowPoint = new SKPoint(baseX, baseY - arrowSize / 2);
                using var path = downArrowPoint.GetDownArrow(arrowSize);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paints.Item1);
            }
        }

        private void DrawPlayerText(SKCanvas canvas, SKPoint point,
                                  string nameText, string distanceText,
                                  string heightText, List<string> rightSideInfo,
                                  bool hasImportantItems, List<LootItem> importantLootItems = null)
        {
            var paints = GetPaints();

            if (this is ObservedPlayer op &&
                MainWindow.MouseoverGroup is int grp &&
                grp == op.SpawnGroupID)
            {
                paints.Item2 = SKPaints.TextMouseoverGroup;
            }

            var spacing = 1 * MainWindow.UIScale;
            var textSize = 12 * MainWindow.UIScale;
            var baseYPosition = point.Y - 12 * MainWindow.UIScale;

            var playerTypeKey = DeterminePlayerTypeKey();
            var typeSettings = Config.PlayerTypeSettings.GetSettings(playerTypeKey);
            var showImportantIndicator = typeSettings.ImportantIndicator && hasImportantItems;

            if (!string.IsNullOrEmpty(nameText))
            {
                var nameWidth = paints.Item2.MeasureText(nameText);
                var namePoint = new SKPoint(point.X - (nameWidth / 2), baseYPosition - 0);

                canvas.DrawText(nameText, namePoint, SKPaints.TextOutline);
                canvas.DrawText(nameText, namePoint, paints.Item2);

                if (showImportantIndicator)
                {
                    var asteriskWidth = SKPaints.TextPulsingAsterisk.MeasureText("*");
                    var verticalOffset = (SKPaints.TextPulsingAsterisk.TextSize - paints.Item2.TextSize) / 2;
                    verticalOffset += 1.5f * MainWindow.UIScale;

                    var asteriskPoint = new SKPoint(
                        namePoint.X - asteriskWidth - (2 * MainWindow.UIScale),
                        namePoint.Y + verticalOffset
                    );

                    canvas.DrawText("*", asteriskPoint, SKPaints.TextPulsingAsteriskOutline);
                    canvas.DrawText("*", asteriskPoint, SKPaints.TextPulsingAsterisk);
                }
            }
            else if (showImportantIndicator)
            {
                var asteriskWidth = SKPaints.TextPulsingAsterisk.MeasureText("*");
                var yPos = point.Y - 2 * MainWindow.UIScale;
                var asteriskPoint = new SKPoint(point.X - (asteriskWidth / 2), yPos);

                canvas.DrawText("*", asteriskPoint, SKPaints.TextPulsingAsteriskOutline);
                canvas.DrawText("*", asteriskPoint, SKPaints.TextPulsingAsterisk);
            }

            var currentBottomY = point.Y + 20 * MainWindow.UIScale;
            if (!string.IsNullOrEmpty(distanceText))
            {
                var distWidth = paints.Item2.MeasureText(distanceText);
                var distPoint = new SKPoint(point.X - (distWidth / 2), currentBottomY);

                canvas.DrawText(distanceText, distPoint, SKPaints.TextOutline);
                canvas.DrawText(distanceText, distPoint, paints.Item2);
            }

            if (importantLootItems?.Any() == true)
            {
                currentBottomY += textSize + spacing;

                foreach (var item in importantLootItems)
                {
                    var itemText = item.ShortName;
                    var itemPaint = GetPlayerLootItemTextPaint(item);
                    var itemWidth = itemPaint.MeasureText(itemText);
                    var itemPoint = new SKPoint(point.X - (itemWidth / 2), currentBottomY);

                    canvas.DrawText(itemText, itemPoint, SKPaints.TextOutline);
                    canvas.DrawText(itemText, itemPoint, itemPaint);

                    currentBottomY += textSize + spacing;
                }
            }

            if (!string.IsNullOrEmpty(heightText))
            {
                var heightWidth = paints.Item2.MeasureText(heightText);
                var heightPoint = new SKPoint(point.X - heightWidth - 15 * MainWindow.UIScale, point.Y + 5 * MainWindow.UIScale);

                canvas.DrawText(heightText, heightPoint, SKPaints.TextOutline);
                canvas.DrawText(heightText, heightPoint, paints.Item2);
            }

            if (rightSideInfo.Count > 0)
            {
                var rightPoint = new SKPoint(
                    point.X + 14 * MainWindow.UIScale,
                    point.Y + 2 * MainWindow.UIScale
                );

                foreach (var line in rightSideInfo)
                {
                    if (string.IsNullOrEmpty(line?.Trim()))
                        continue;

                    canvas.DrawText(line, rightPoint, SKPaints.TextOutline);
                    canvas.DrawText(line, rightPoint, paints.Item2);
                    rightPoint.Offset(0, textSize);
                }
            }
        }

        /// <summary>
        /// Draws a Player Marker on this location with type-specific settings
        /// </summary>
        private void DrawPlayerMarker(SKCanvas canvas, ILocalPlayer localPlayer, SKPoint point, PlayerTypeSettings typeSettings)
        {
            var radians = MapRotation.ToRadians();
            var paints = GetPaints();

            if (this is ObservedPlayer op &&
                this != localPlayer &&
                MainWindow.MouseoverGroup is int grp &&
                grp == op.SpawnGroupID)
            {
                paints.Item1 = SKPaints.PaintMouseoverGroup;
            }

            SKPaints.ShapeOutline.StrokeWidth = paints.Item1.StrokeWidth + 2f * MainWindow.UIScale;

            var size = 6 * MainWindow.UIScale;
            canvas.DrawCircle(point, size, SKPaints.ShapeOutline);
            canvas.DrawCircle(point, size, paints.Item1);

            var aimlineLength = typeSettings.AimlineLength;

            if (typeSettings.HighAlert && !IsFriendly && this.IsFacingTarget(localPlayer, typeSettings.RenderDistance))
                aimlineLength = 9999;

            var aimlineEnd = GetAimlineEndpoint(point, radians, aimlineLength);
            canvas.DrawLine(point, aimlineEnd, SKPaints.ShapeOutline);
            canvas.DrawLine(point, aimlineEnd, paints.Item1);
        }

        /// <summary>
        /// Draws a Death Marker on this location.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawDeathMarker(SKCanvas canvas, SKPoint point, SKColor color)
        {
            var length = 6 * MainWindow.UIScale;

            using var corpseLinePaint = new SKPaint
            {
                Color = color,
                StrokeWidth = SKPaints.PaintDeathMarker.StrokeWidth,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };

            canvas.DrawLine(new SKPoint(point.X - length, point.Y + length),
                new SKPoint(point.X + length, point.Y - length), corpseLinePaint);
            canvas.DrawLine(new SKPoint(point.X - length, point.Y - length),
                new SKPoint(point.X + length, point.Y + length), corpseLinePaint);
        }

        /// <summary>
        /// Gets the point where the Aimline 'Line' ends. Applies UI Scaling internally.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static SKPoint GetAimlineEndpoint(SKPoint start, float radians, float aimlineLength)
        {
            aimlineLength *= MainWindow.UIScale;
            return new SKPoint(start.X + MathF.Cos(radians) * aimlineLength,
                start.Y + MathF.Sin(radians) * aimlineLength);
        }

        private SKColor GetCorpseFilterColor()
        {
            if (LootObject?.Loot != null && LootObject.Loot.Any())
            {
                var topItem = LootObject.Loot.OrderLoot().FirstOrDefault();

                if (topItem != null)
                {
                    var matchedFilter = topItem.MatchedFilter;
                    if (matchedFilter != null && !string.IsNullOrEmpty(matchedFilter.Color))
                    {
                        if (SKColor.TryParse(matchedFilter.Color, out var filterColor))
                            return filterColor;
                    }

                    if (topItem is QuestItem || (Config.QuestHelper.Enabled && topItem.IsQuestCondition))
                        return SKPaints.PaintQuestItem.Color;

                    if (topItem.IsWishlisted)
                        return SKPaints.PaintWishlistItem.Color;

                    if (topItem.IsValuableLoot)
                        return SKPaints.PaintImportantLoot.Color;
                }
            }

            return SKPaints.PaintDeathMarker.Color;
        }

        /// <summary>
        /// Helper method to get the appropriate text paint for a player's loot item based on its importance/filter
        /// </summary>
        private static SKPaint GetPlayerLootItemTextPaint(LootItem item)
        {
            var isImportant = item.IsImportant ||
                               item is QuestItem ||
                               (Config.QuestHelper.Enabled && item.IsQuestCondition) ||
                               (LootFilterControl.ShowBackpacks && item.IsBackpack) ||
                               (LootFilterControl.ShowMeds && item.IsMeds) ||
                               (LootFilterControl.ShowFood && item.IsFood) ||
                               (LootFilterControl.ShowWeapons && item.IsWeapon) ||
                               item.IsValuableLoot;

            if (isImportant)
            {
                var paints = item.GetPaints();
                return paints.Item2;
            }

            return SKPaints.TextMouseover;
        }

        public ValueTuple<SKPaint, SKPaint> GetPaints()
        {
            if (IsAimbotLocked)
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintAimbotLocked, SKPaints.TextAimbotLocked);

            if (IsFocused)
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintFocused, SKPaints.TextFocused);

            if (this is LocalPlayer)
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintLocalPlayer, SKPaints.TextLocalPlayer);

            switch (Type)
            {
                case PlayerType.Teammate:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintTeammate, SKPaints.TextTeammate);
                case PlayerType.USEC:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintUSEC, SKPaints.TextUSEC);
                case PlayerType.BEAR:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintBEAR, SKPaints.TextBEAR);
                case PlayerType.AIScav:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintScav, SKPaints.TextScav);
                case PlayerType.AIRaider:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintRaider, SKPaints.TextRaider);
                case PlayerType.AIBoss:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintBoss, SKPaints.TextBoss);
                case PlayerType.PScav:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintPScav, SKPaints.TextPScav);
                case PlayerType.SpecialPlayer:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintSpecial, SKPaints.TextSpecial);
                case PlayerType.Streamer:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintStreamer, SKPaints.TextStreamer);
                default:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintUSEC, SKPaints.TextUSEC);
            }
        }

        public void DrawMouseover(SKCanvas canvas, XMMapParams mapParams, LocalPlayer localPlayer)
        {
            if (this == localPlayer)
                return;

            var playerTypeKey = DeterminePlayerTypeKey();
            var typeSettings = Config.PlayerTypeSettings.GetSettings(playerTypeKey);

            var lines = new List<(string text, SKPaint paint)>();
            var name = Config.MaskNames && IsHuman ? "<Hidden>" : Name;
            string health = null;
            string kd = null;

            if (this is ObservedPlayer observed)
            {
                health = observed.HealthStatus is Enums.ETagStatus.Healthy
                    ? null
                    : $" ({observed.HealthStatus.GetDescription()})"; // Only display abnormal health status

                if (observed.Profile?.Overall_KD is float kdResult)
                    kd = kdResult.ToString("n2");
            }

            if (IsStreaming) // Streamer Notice
                lines.Add(($"[LIVE - Double Click]", SKPaints.TextMouseover));

            var alert = this.Alerts?.Trim();

            if (!string.IsNullOrEmpty(alert))
                lines.Add((alert, SKPaints.TextMouseover));

            if (IsHostileActive)
            {
                lines.Add(($"{name}{health}", SKPaints.TextMouseover));
                lines.Add(($"KD: {kd}", SKPaints.TextMouseover));
                var gear = Gear;
                var hands = $"{Hands?.CurrentItem} {Hands?.CurrentAmmo}".Trim();
                lines.Add(($"Use: {(hands is null ? "--" : hands)}", SKPaints.TextMouseover));
                var faction = PlayerSide.ToString();
                string g = null;

                if (this is ObservedPlayer op)
                {
                    if (op.SpawnGroupID != -1)
                        g = $" SG:{op.SpawnGroupID} ";
                    else if (op.NetworkGroupID != -1)
                        g = $" NG:{op.NetworkGroupID} ";
                }

                lines.Add(($"{faction}{g}", SKPaints.TextMouseover));

                var loot = gear?.Loot;

                if (loot is not null)
                {
                    var playerValue = TarkovMarketItem.FormatPrice(gear?.Value ?? -1);
                    lines.Add(($"Value: {playerValue}", SKPaints.TextMouseover));
                    var iterations = 0;

                    foreach (var item in loot)
                    {
                        if (iterations++ >= 5)
                            break;

                        var itemPaint = GetPlayerLootItemTextPaint(item);
                        lines.Add((item.GetUILabel(), itemPaint));
                    }
                }
            }
            else if (!IsAlive)
            {
                lines.Add(($"{Type.GetDescription()}:{name}", SKPaints.TextMouseover));
            
                string g = null;
            
                if (this is ObservedPlayer op)
                {
                    if (op.SpawnGroupID != -1)
                        g = $"SG:{op.SpawnGroupID}";
                    else if (op.NetworkGroupID != -1)
                        g = $"NG:{op.NetworkGroupID}";
                }
            
                if (g != null)
                    lines.Add((g, SKPaints.TextMouseover));

                var corpseLoot = LootObject?.Loot?.OrderLoot();

                if (corpseLoot is not null)
                {
                    var sumPrice = corpseLoot.Sum(x => x.Price);
                    var corpseValue = TarkovMarketItem.FormatPrice(sumPrice);
                    lines.Add(($"Value: {corpseValue}", SKPaints.TextMouseover));

                    if (corpseLoot.Any())
                    {
                        foreach (var item in corpseLoot)
                        {
                            var itemPaint = GetPlayerLootItemTextPaint(item);
                            lines.Add((item.GetUILabel(), itemPaint));
                        }
                    }
                    else
                    {
                        lines.Add(("Empty", SKPaints.TextMouseover));
                    }
                }
            }
            else if (IsAIActive)
            {
                lines.Add((name, SKPaints.TextMouseover));
            }

            Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines);
        }

        public void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (this == localPlayer || !IsActive || !IsAlive)
                return;
        
            if (!Skeleton.HasValidPosition)
                return;
        
            var espSettings = ESP.Config.PlayerTypeESPSettings
                .GetSettings(DeterminePlayerTypeKey());
        
            float dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > espSettings.RenderDistance)
                return;
        
            if (!CameraManagerBase.WorldToScreen(ref Position, out var baseScreen))
                return;
        
            var paints = GetESPPaints();
            var renderMode = espSettings.RenderMode;
        
            // ---------------- BTR ----------------
            if (renderMode != ESPPlayerRenderMode.None && this is BtrOperator btr)
            {
                if (CameraManagerBase.WorldToScreen(ref btr.Position, out var btrScreen))
                    btrScreen.DrawESPText(canvas, btr, localPlayer, espSettings.ShowDistance, paints.Item2, "BTR");
                return;
            }
        
            // ---------------- BOX ----------------
            var box = Skeleton.GetESPBox(baseScreen);
            if (box == null)
                return;
        
            var playerBox = box.Value;
            var headPoint = new SKPoint(playerBox.MidX, playerBox.Top);
        
            // ---------------- SKELETON ----------------
            if (renderMode == ESPPlayerRenderMode.Bones)
            {
                if (!Skeleton.UpdateESPBuffer())
                    return;
        
                for (int i = 0; i < SkeletonSegments.Length; i++)
                {
                    int idx = i * 2;
                    if (idx + 1 >= Skeleton.ESPBuffer.Length)
                        break;
        
                    var p0 = Skeleton.ESPBuffer[idx];
                    var p1 = Skeleton.ESPBuffer[idx + 1];
        
                    // HARD GUARD ¡ú prevents long diagonal lines
                    if (!p0.IsFinite() || !p1.IsFinite())
                        continue;
        
                    var (b0, b1) = SkeletonSegments[i];
        
                    bool v0 = BoneVisibility.TryGetValue(b0, out var s0) && s0;
                    bool v1 = BoneVisibility.TryGetValue(b1, out var s1) && s1;
        
                    var paint = (v0 && v1)
                        ? SKPaints.PaintVisible
                        : paints.Item1;
        
                    canvas.DrawLine(p0, p1, paint);
                }
            }
            else if (renderMode == ESPPlayerRenderMode.Box)
            {
                canvas.DrawRect(playerBox, paints.Item1);
            }
            else if (renderMode == ESPPlayerRenderMode.HeadDot)
            {
                if (CameraManagerBase.WorldToScreen(
                        ref Skeleton.Bones[Bones.HumanHead].Position,
                        out var headScreen, true, true))
                {
                    canvas.DrawCircle(headScreen, 1.5f * ESP.Config.FontScale, paints.Item1);
                }
                else
                {
                    canvas.DrawCircle(headPoint, 1.5f * ESP.Config.FontScale, paints.Item1);
                }
            }
        
            if (BattleMode)
                return;
        
            // ---------------- TEXT ----------------
            var observed = this as ObservedPlayer;
            float textY = headPoint.Y - 5f * ESP.Config.FontScale;
            float lineHeight = paints.Item2.TextSize * 1.2f * ESP.Config.FontScale;
        
            if (espSettings.ShowADS && IsAiming && observed != null)
            {
                canvas.DrawText("ADS", new SKPoint(headPoint.X, textY), paints.Item2);
                textY -= lineHeight;
            }
        
            if (espSettings.ShowName)
            {
                string name = Name;
                if (IsHostilePmc)
                    name = (PlayerSide == Enums.EPlayerSide.Usec ? "U:" : "B:") + name;
        
                canvas.DrawText(name, new SKPoint(headPoint.X, textY), paints.Item2);
            }
        
            if (espSettings.ShowHealth && observed != null)
                DrawHealthBar(canvas, observed, playerBox);
        
            // ---------------- BOTTOM INFO ----------------
            if (!espSettings.ShowDistance &&
                !espSettings.ShowWeapon &&
                !espSettings.ShowAmmoType &&
                !espSettings.ShowKD &&
                !espSettings.ShowNVG &&
                !espSettings.ShowThermal &&
                !espSettings.ShowUBGL)
                return;
        
            var lines = new string[6];
            int count = 0;
        
            if (espSettings.ShowDistance)
                lines[count++] = $"{(int)dist}m";
        
            if (espSettings.ShowWeapon && Hands?.CurrentItem != null)
                lines[count++] = espSettings.ShowAmmoType && Hands.CurrentAmmo != null
                    ? $"{Hands.CurrentItem}/{Hands.CurrentAmmo}"
                    : Hands.CurrentItem;
        
            if (espSettings.ShowKD && observed?.Profile?.Overall_KD is float kd &&
                kd >= espSettings.MinKD)
                lines[count++] = kd.ToString("n2");
        
            if (espSettings.ShowNVG && Gear?.HasNVG == true)
                lines[count++] = "NVG";
        
            if (espSettings.ShowThermal && Gear?.HasThermal == true)
                lines[count++] = "THERMAL";
        
            if (espSettings.ShowUBGL && Gear?.HasUBGL == true)
                lines[count++] = "UBGL";
        
            if (count > 0)
            {
                var label = string.Join("\n", lines[..count]);
                var metrics = paints.Item2.FontMetrics;

                // Ascent is negative in Skia
                float ascent = -metrics.Ascent * ESP.Config.FontScale;
                float lineAdvance = (metrics.Descent - metrics.Ascent) * 0.9f * ESP.Config.FontScale;

                float totalHeight = (count - 1) * lineAdvance + ascent;
                float padding = 1f * ESP.Config.FontScale;

                // Baseline placed so TOP of first line starts just under box
                float textBaseY = playerBox.Bottom + padding + ascent;

                new SKPoint(playerBox.MidX, textBaseY)
                    .DrawESPText(canvas, this, localPlayer, false, paints.Item2, label, null);
            }
        }
        private static readonly (Bones Start, Bones End)[] SkeletonSegments =
        {
            (Bones.HumanHead, Bones.HumanNeck),
            (Bones.HumanNeck, Bones.HumanSpine3),
            (Bones.HumanSpine3, Bones.HumanSpine2),
            (Bones.HumanSpine2, Bones.HumanSpine1),
            (Bones.HumanSpine1, Bones.HumanPelvis),
        
            (Bones.HumanPelvis, Bones.HumanLThigh2),
            (Bones.HumanLThigh2, Bones.HumanLFoot),
        
            (Bones.HumanPelvis, Bones.HumanRThigh2),
            (Bones.HumanRThigh2, Bones.HumanRFoot),
        
            (Bones.HumanLCollarbone, Bones.HumanLForearm2),
            (Bones.HumanLForearm2, Bones.HumanLPalm),
        
            (Bones.HumanRCollarbone, Bones.HumanRForearm2),
            (Bones.HumanRForearm2, Bones.HumanRPalm),
        };

        public void UpdateBoneVisibility(Bones[] bones, bool[] results)
        {
            if (bones.Length != results.Length)
                return;

            for (int i = 0; i < bones.Length; i++)
            {
                BoneVisibility[bones[i]] = results[i];
                //XMLogging.WriteLine($"Bone {bones[i]} visibility: {results[i]}"); // Your log line
            }
        }

        private SKPoint GetScreenPointForBone(Bones bone)
        {
            // If you store the bone screen points in a dictionary or buffer, use that
            // For example, you might do something like:
            if (!this.Skeleton.Bones.TryGetValue(bone, out var transform))
                return default;
        
            if (!CameraManagerBase.WorldToScreen(ref transform.Position, out var screenPos))
                return default;
        
            return new SKPoint(screenPos.X, screenPos.Y);
        }

        /// <summary>
        /// Draws a health bar to the left of the player
        /// </summary>
        private void DrawHealthBar(SKCanvas canvas, ObservedPlayer player, SKRect playerBounds)
        {
            var healthPercent = GetHealthPercentage(player);
            var healthColor = GetHealthColor(player.HealthStatus);
            var barWidth = 3f * ESP.Config.FontScale;
            var barHeight = playerBounds.Height; // Use full height of the player box
            var barOffsetX = 6f * ESP.Config.FontScale;

            var left = playerBounds.Left - barOffsetX - barWidth;
            var top = playerBounds.Top; // Align with the top of the player box

            var bgRect = new SKRect(left, top, left + barWidth, top + barHeight);

            canvas.DrawRect(bgRect, SKPaints.PaintESPHealthBarBg);

            var filledHeight = barHeight * healthPercent;
            var bottom = top + barHeight;
            var fillTop = bottom - filledHeight;
            var fillRect = new SKRect(left, fillTop, left + barWidth, bottom);

            var healthFillPaint = SKPaints.PaintESPHealthBar.Clone();
            healthFillPaint.Color = healthColor;

            canvas.DrawRect(fillRect, healthFillPaint);
            canvas.DrawRect(bgRect, SKPaints.PaintESPHealthBarBorder);
        }

        /// <summary>
        /// Gets health color based on player's health status
        /// </summary>
        private SKColor GetHealthColor(Enums.ETagStatus healthStatus)
        {
            return healthStatus switch
            {
                Enums.ETagStatus.Healthy => new SKColor(0, 255, 0),     // Green
                Enums.ETagStatus.Injured => new SKColor(255, 255, 0),   // Yellow
                Enums.ETagStatus.BadlyInjured => new SKColor(255, 165, 0), // Orange
                Enums.ETagStatus.Dying => new SKColor(255, 0, 0),       // Red
                _ => new SKColor(0, 255, 0)
            };
        }

        /// <summary>
        /// Gets health percentage based on observed player's health status
        /// This is a simplified approach - ideally would use actual health values if available
        /// </summary>
        private float GetHealthPercentage(ObservedPlayer player)
        {
            return player.HealthStatus switch
            {
                Enums.ETagStatus.Healthy => 1.0f,
                Enums.ETagStatus.Injured => 0.75f,
                Enums.ETagStatus.BadlyInjured => 0.4f,
                Enums.ETagStatus.Dying => 0.15f,
                _ => 1.0f
            };
        }

        // <summary>
        // Gets Aimview drawing paintbrushes based on this Player Type.
        // </summary>
        private ValueTuple<SKPaint, SKPaint> GetESPPaints()
        {
            if (IsAimbotLocked)
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintAimbotLockedESP, SKPaints.TextAimbotLockedESP);

            if (IsFocused)
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintFocusedESP, SKPaints.TextFocusedESP);

            //if (IsVisible)
            //    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintVisible, SKPaints.TextVisible);

            switch (Type)
            {
                case PlayerType.Teammate:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintFriendlyESP, SKPaints.TextFriendlyESP);
                case PlayerType.USEC:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintUSECESP, SKPaints.TextUSECESP);
                case PlayerType.BEAR:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintBEARESP, SKPaints.TextBEARESP);
                case PlayerType.AIScav:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintScavESP, SKPaints.TextScavESP);
                case PlayerType.AIRaider:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintRaiderESP, SKPaints.TextRaiderESP);
                case PlayerType.AIBoss:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintBossESP, SKPaints.TextBossESP);
                case PlayerType.PScav:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintPlayerScavESP, SKPaints.TextPlayerScavESP);
                case PlayerType.SpecialPlayer:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintSpecialESP, SKPaints.TextSpecialESP);
                case PlayerType.Streamer:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintStreamerESP, SKPaints.TextStreamerESP);
                default:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintUSECESP, SKPaints.TextUSECESP);
            }
        }

        // <summary>
        // Gets mini radar paint brush based on this Player Type.
        // </summary>
        public SKPaint GetMiniRadarPaint()
        {
            if (IsAimbotLocked)
                return SKPaints.PaintMiniAimbotLocked;

            if (IsFocused)
                return SKPaints.PaintMiniFocused;

            if (this is LocalPlayer)
                return SKPaints.PaintMiniLocalPlayer;

            switch (Type)
            {
                case PlayerType.Teammate:
                    return SKPaints.PaintMiniTeammate;
                case PlayerType.USEC:
                    return SKPaints.PaintMiniUSEC;
                case PlayerType.BEAR:
                    return SKPaints.PaintMiniBEAR;
                case PlayerType.AIScav:
                    return SKPaints.PaintMiniScav;
                case PlayerType.AIRaider:
                    return SKPaints.PaintMiniRaider;
                case PlayerType.AIBoss:
                    return SKPaints.PaintMiniBoss;
                case PlayerType.PScav:
                    return SKPaints.PaintMiniPScav;
                case PlayerType.SpecialPlayer:
                    return SKPaints.PaintMiniSpecial;
                case PlayerType.Streamer:
                    return SKPaints.PaintMiniStreamer;
                default:
                    return SKPaints.PaintMiniUSEC;
            }
        }

        /// <summary>
        /// Determine player type key for settings lookup
        /// </summary>
        public string DeterminePlayerTypeKey()
        {
            if (this is LocalPlayer)
                return "LocalPlayer";

            if (IsAimbotLocked)
                return "AimbotLocked";

            if (IsFocused)
                return "Focused";

            return Type.ToString();
        }

        #endregion

        #region Types

        /// <summary>
        /// Defines Player Unit Type (Player,PMC,Scav,etc.)
        /// </summary>
        public enum PlayerType
        {
            /// <summary>
            /// Default value if a type cannot be established.
            /// </summary>
            [Description("Default")]
            Default,
            /// <summary>
            /// Teammate of LocalPlayer.
            /// </summary>
            [Description("Teammate")]
            Teammate,
            /// <summary>
            /// Hostile/Enemy USEC.
            /// </summary>
            [Description("USEC")]
            USEC,
            /// <summary>
            /// Hostile/Enemy BEAR.
            /// </summary>
            [Description("BEAR")]
            BEAR,
            /// <summary>
            /// Normal AI Bot Scav.
            /// </summary>
            [Description("Scav")]
            AIScav,
            /// <summary>
            /// Difficult AI Raider.
            /// </summary>
            [Description("Raider")]
            AIRaider,
            /// <summary>
            /// Difficult AI Boss.
            /// </summary>
            [Description("Boss")]
            AIBoss,
            /// <summary>
            /// Player controlled Scav.
            /// </summary>
            [Description("Player Scav")]
            PScav,
            /// <summary>
            /// 'Special' Human Controlled Hostile PMC/Scav (on the watchlist, or a special account type).
            /// </summary>
            [Description("Special Player")]
            SpecialPlayer,
            /// <summary>
            /// Human Controlled Hostile PMC/Scav that has a Twitch account name as their IGN.
            /// </summary>
            [Description("Streamer")]
            Streamer
        }

        #endregion
    }
}