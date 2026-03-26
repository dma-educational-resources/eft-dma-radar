using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.Collections;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.EFTPlayer.SpecialCollections;
using eft_dma_radar.Tarkov.Features.MemoryWrites.Patches;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Web.ProfileApi;
using static SDK.Enums;
using static SDK.Offsets;

namespace eft_dma_radar.Tarkov.EFTPlayer
{
    public class ObservedPlayer : Player
    {
        /// <summary>
        /// Player's Profile & Stats (If Human Player).
        /// </summary>
        public PlayerProfile Profile { get; }
        /// <summary>
        /// ObservedPlayerController for non-clientplayer players.
        /// </summary>
        private ulong ObservedPlayerController { get; }
        /// <summary>
        /// ObservedHealthController for non-clientplayer players.
        /// </summary>
        private ulong ObservedHealthController { get; }
        /// <summary>
        /// Player name.
        /// </summary>
        public override string Name { get; set; }
        /// <summary>
        /// Account UUID for Human Controlled Players.
        /// </summary>
        public override string AccountID { get; set; }
        /// <summary>
        /// Deprecated
        /// </summary>
        public override int GroupID { get; } = -1;
        public override string ProfileID { get; set; }
        /// <summary>
        /// EFT network squad (real teammates only)
        /// </summary>
        public override int NetworkGroupID { get; }
        private bool _identityApplied = false;

        /// <summary>
        /// Logical spawn-based group (hostiles included)
        /// </summary>
        public override int SpawnGroupID
        {
            get
            {
                if (!IsHuman || string.IsNullOrEmpty(ProfileID))
                    return -1;
        
                return PlayerListWorker.GetOrAssignSpawnGroup(
                    ProfileID,
                    Position,
                    PlayerSide);
            }
        }     

        /// <summary>
        /// Player's Faction.
        /// </summary>
        public override Enums.EPlayerSide PlayerSide { get; }
        /// <summary>
        /// Player is Human-Controlled.
        /// </summary>
        public override bool IsHuman { get; }
        /// <summary>
        /// MovementContext / StateContext
        /// </summary>
        public override ulong MovementContext { get; }
        /// <summary>
        /// EFT.PlayerBody
        /// </summary>
        public override ulong Body { get; }
        /// <summary>
        /// Inventory Controller field address.
        /// </summary>
        public override ulong InventoryControllerAddr { get; }
        /// <summary>
        /// Hands Controller field address.
        /// </summary>
        public override ulong HandsControllerAddr { get; }
        /// <summary>
        /// Corpse field address..
        /// </summary>
        public override ulong CorpseAddr { get; }
        /// <summary>
        /// Player Rotation Field Address (view angles).
        /// </summary>
        public override ulong RotationAddress { get; }
        private static int _usecCounter = 0;
        private static int _bearCounter = 0;

        // Key: Player.Base (ulong) → assigned index
        private static readonly Dictionary<ulong, int> _pmcIndex = [];
        private static readonly Lock _pmcLock = new();
        private int GetOrAssignPmcIndex(bool isUsec)
        {
            lock (_pmcLock)
            {
                if (_pmcIndex.TryGetValue(this, out int existing))
                    return existing;

                int index = isUsec
                    ? ++_usecCounter
                    : ++_bearCounter;

                _pmcIndex[this] = index;
                return index;
            }
        }               
        /// <summary>
        /// Player's Skeleton Bones.
        /// </summary>
        public override Skeleton Skeleton { get; protected set; }
        public override int VoipId { get; }  
        private static int ParseVoipId(ulong baseAddr)
        {
            try
            {
                ulong strPtr = Memory.ReadPtr(baseAddr + Offsets.ObservedPlayerView.VoipId);
                if (strPtr == 0)
                    return -1;

                string s = Memory.ReadUnityString(strPtr);
                if (string.IsNullOrWhiteSpace(s))
                    return -1;

                return int.TryParse(s, out int id) ? id : -1;
            }
            catch
            {
                return -1;
            }
        }     
        public bool TryEnsureSkeleton()
        {
            if (Skeleton != null)
                return true;

            try
            {
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
        /// Player's Current Health Status
        /// </summary>
        public Enums.ETagStatus HealthStatus { get; private set; } = Enums.ETagStatus.Healthy;

        internal ObservedPlayer(ulong playerBase) : base(playerBase)
        {
            var localPlayer = Memory.LocalPlayer;
            ArgumentNullException.ThrowIfNull(localPlayer, nameof(localPlayer));
            ObservedPlayerController = Memory.ReadPtr(this + Offsets.ObservedPlayerView.ObservedPlayerController);
            ArgumentOutOfRangeException.ThrowIfNotEqual(this,
                Memory.ReadValue<ulong>(ObservedPlayerController + Offsets.ObservedPlayerController.Player),
                nameof(ObservedPlayerController));
            ObservedHealthController = Memory.ReadPtr(ObservedPlayerController + Offsets.ObservedPlayerController.HealthController);
            ArgumentOutOfRangeException.ThrowIfNotEqual(this,
                Memory.ReadValue<ulong>(ObservedHealthController + Offsets.ObservedHealthController.Player),
                nameof(ObservedHealthController));
            Body = Memory.ReadPtr(this + Offsets.ObservedPlayerView.PlayerBody);
            InventoryControllerAddr = ObservedPlayerController + Offsets.ObservedPlayerController.InventoryController;
            HandsControllerAddr = ObservedPlayerController + Offsets.ObservedPlayerController.HandsController;
            CorpseAddr = ObservedHealthController + Offsets.ObservedHealthController.PlayerCorpse;
            VoipId = ParseVoipId(this);

            NetworkGroupID = GetNetworkGroupID();
            MovementContext = GetMovementContext();
            RotationAddress = ValidateRotationAddr(MovementContext + Offsets.ObservedMovementController.Rotation);

            /// Determine Player Type
            PlayerSide = (Enums.EPlayerSide)Memory.ReadValue<int>(this + Offsets.ObservedPlayerView.Side); // Usec,Bear,Scav,etc.
            if (!Enum.IsDefined(PlayerSide)) // Make sure PlayerSide is valid
                throw new Exception("Invalid Player Side/Faction!");

            var isAI = Memory.ReadValue<bool>(this + Offsets.ObservedPlayerView.IsAI);
            IsHuman = !isAI;
            if (IsScav)
            {
                if (isAI)
                {
                    var gearMgr = new GearManager(this, this.IsPmc);

                    // =====================================================
                    // 1) SANTA DETECTION (FIRST, AUTHORITATIVE, SLOT-AGNOSTIC)
                    // =====================================================
                    bool isSanta = false;

                    foreach (var kv in gearMgr.Equipment)
                    {
                        var item = kv.Value;
                        if (item == null)
                            continue;

                        var name = item.Short?.ToLowerInvariant();
                        if (string.IsNullOrEmpty(name))
                            continue;

                        // Primary signal: Santa bag
                        if (name.Contains("santa") && name.Contains("bag"))
                        {
                            isSanta = true;
                            break;
                        }

                        // Secondary safety net (optional but recommended)
                        // Covers Santa face cover / odd localization cases
                        if (name.Contains("santa"))
                        {
                            isSanta = true;
                            break;
                        }
                    }

                    if (isSanta)
                    {
                        Name = "Santa";
                        Type = PlayerType.AIBoss; // or Special / AIRaider if you prefer
                        goto DoneAIClassification;
                    }

                    // =====================================================
                    // 2) NORMAL AI ROLE (VOICE-BASED, NON-AUTHORITATIVE)
                    // =====================================================
                    var voicePtr = Memory.ReadPtr(this + Offsets.ObservedPlayerView.Voice);
                    string voice = Memory.ReadUnityString(voicePtr);
                    var role = Player.GetAIRoleInfo(voice);

                    Name = role.Name;
                    Type = role.Type;

                    // =====================================================
                    // 3) SPECIAL MAP OVERRIDES
                    // =====================================================
                    switch (Name)
                    {
                        case "Priest":
                            if (gearMgr.Equipment.TryGetValue("FaceCover", out var fc) &&
                                fc.Short.Equals("zryachiy", StringComparison.OrdinalIgnoreCase))
                            {
                                Name = "Zryachiy";
                            }
                            break;

                        case "Usec":
                        case "Bear":
                            if (Memory.MapID.Equals("lighthouse", StringComparison.OrdinalIgnoreCase))
                            {
                                Name = "Rogue";
                                Type = PlayerType.AIRaider;
                            }
                            else if (Memory.MapID.Equals("rezervbase", StringComparison.OrdinalIgnoreCase))
                            {
                                Name = "Raider";
                                Type = PlayerType.AIRaider;
                            }
                            break;
                    }

                    if (Memory.MapID.Equals("laboratory", StringComparison.OrdinalIgnoreCase))
                    {
                        Name = "Raider";
                        Type = PlayerType.AIRaider;
                    }

                    // =====================================================
                    // 4) GUARD OVERRIDE (LAST)
                    // =====================================================
                    if (GuardManager.TryIdentifyGuard(
                        gearMgr,
                        new HandsManager(this),
                        Memory.MapID,
                        Type))
                    {
                        Name = "Guard";
                        Type = PlayerType.AIRaider;
                    }

                DoneAIClassification:;
                }
                else
                {
                    string nickname = null;
                    try
                    {
                        var nickPtr = Memory.ReadPtr(this + Offsets.ObservedPlayerView.NickName);
                        if (nickPtr != 0)
                            nickname = Memory.ReadUnityString(nickPtr);
                    }
                    catch { }

                    if (!string.IsNullOrWhiteSpace(nickname))
                    {
                        Name = nickname;
                    }
                    else
                    {
                        int pscavNumber = Interlocked.Increment(ref _playerScavNumber);
                        Name = $"PScav{pscavNumber}";
                    }

                    Type = GroupID != -1 && GroupID == localPlayer.GroupID
                        ? PlayerType.Teammate
                        : PlayerType.PScav;
                }
            }

            else if (IsPmc)
            {
                bool isTeammate =
                    NetworkGroupID != -1 &&
                    NetworkGroupID == localPlayer.NetworkGroupID;

                // Try to read the actual nickname from memory
                string nickname = null;
                try
                {
                    var nickPtr = Memory.ReadPtr(this + Offsets.ObservedPlayerView.NickName);
                    if (nickPtr != 0)
                        nickname = Memory.ReadUnityString(nickPtr);
                }
                catch { }

                if (!string.IsNullOrWhiteSpace(nickname))
                {
                    Name = nickname;
                }
                else
                {
                    int pmcIndex = GetOrAssignPmcIndex(PlayerSide == EPlayerSide.Usec);
                    Name = PlayerSide == EPlayerSide.Usec
                        ? $"U:PMC{pmcIndex}"
                        : $"B:PMC{pmcIndex}";
                }

                Type = isTeammate
                    ? PlayerType.Teammate
                    : (PlayerSide == EPlayerSide.Usec
                        ? PlayerType.USEC
                        : PlayerType.BEAR);
            }

            else
                throw new NotImplementedException(nameof(PlayerSide));

            if (IsHuman)
            {
                var handController = Memory.ReadPtr(HandsControllerAddr);
                var dickController = Memory.ReadPtr(handController + Offsets.ObservedHandsController.BundleAnimationBones);
                this.PWA =  Memory.ReadPtr(dickController + Offsets.BundleAnimationBonesController.ProceduralWeaponAnimationObs);
                Profile = new PlayerProfile(this);
            }

            PlayerHistory.AddOrUpdate(this);
        }


        /// <summary>
        /// Gets player's Group Number.
        /// </summary>
        private int GetNetworkGroupID()
        {
            try
            {
                var grpIdPtr = Memory.ReadPtr(this + Offsets.ObservedPlayerView.GroupID);
                var grp = Memory.ReadUnityString(grpIdPtr);
                return _groups.GetGroup(grp);
            }
            catch
            {
                return -1;
            }
        }



        public void CheckIfStreaming()
        {
            if (string.IsNullOrEmpty(StreamingURL))
            {
                IsStreaming = false;

                if (Type == PlayerType.Streamer)
                {
                    UpdatePlayerType(PlayerType.SpecialPlayer);

                    if (PlayerWatchlist.Entries.TryGetValue(AccountID, out var entry))
                    {
                        ClearAlerts();
                        UpdateAlerts(entry.Reason);
                    }
                }
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    if (!PlayerWatchlist.Entries.TryGetValue(AccountID, out var watchlistEntry))
                        return;

                    var wasStreaming = IsStreaming;
                    string alertReason = watchlistEntry.Reason;

                    if (watchlistEntry.StreamingPlatform != StreamingPlatform.None &&
                        !string.IsNullOrEmpty(watchlistEntry.Username))
                    {
                        IsStreaming = await StreamingUtils.IsLive(watchlistEntry.StreamingPlatform, watchlistEntry.Username);
                    }
                    else
                    {
                        IsStreaming = false;
                    }

                    if (IsStreaming != wasStreaming)
                    {
                        if (IsStreaming)
                        {
                            UpdatePlayerType(PlayerType.Streamer);
                            ClearAlerts();
                            UpdateAlerts(alertReason);
                        }
                        else if (Type == PlayerType.Streamer)
                        {
                            UpdatePlayerType(PlayerType.SpecialPlayer);
                            ClearAlerts();
                            UpdateAlerts(alertReason);

                            XMLogging.WriteLine($"[Streaming] {Name} ({AccountID}) is no longer streaming");
                        }
                    }
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[Streaming] Error checking if {Name} [{AccountID}] is live: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Get Movement Context Instance.
        /// </summary>
        private ulong GetMovementContext()
        {
            var movementController = Memory.ReadPtrChain(ObservedPlayerController, Offsets.ObservedPlayerController.MovementController);
            return movementController;
        }

        /// <summary>
        /// Refresh Player Information.
        /// </summary>
        public override void OnRegRefresh(ScatterReadIndex index, IReadOnlySet<ulong> registered, bool? isActiveParam = null)
        {
            if (isActiveParam is not bool isActive)
                isActive = registered.Contains(this);

            if (isActive)
            {
                if (IsHuman)
                {
                    UpdateMemberCategory();
                    UpdatePlayerName();
                    // Check player rules after profile data is loaded
                    //CheckPlayerRules();
                }

                UpdateHealthStatus();
                UpdateAimingStatus();
            }
            base.OnRegRefresh(index, registered, isActive);
        }

        private void UpdatePlayerName()
        {
            if (IsAI)
                return;

            if (!_identityApplied)
            {
                try
                {
                    var nickPtr = Memory.ReadPtr(this + Offsets.ObservedPlayerView.NickName);
                    if (nickPtr != 0)
                    {
                        var nickname = Memory.ReadUnityString(nickPtr);
                        if (!string.IsNullOrWhiteSpace(nickname))
                        {
                            Name = nickname;
                            _identityApplied = true;
                            PlayerHistory.AddOrUpdate(this);
                        }
                    }
                }
                catch { }

                // Try PlayerList.json identity
                if (!_identityApplied && !string.IsNullOrEmpty(ProfileID))
                {
                    if (PlayerListWorker.TryGetIdentity(
                            ProfileID,
                            out var plNickname,
                            out var plAccountId))
                    {
                        if (!string.IsNullOrWhiteSpace(plNickname))
                            Name = plNickname;

                        if (!string.IsNullOrWhiteSpace(plAccountId))
                            AccountID = plAccountId;

                        _identityApplied = true;
                        PlayerHistory.AddOrUpdate(this);

                        XMLogging.WriteLine(
                            $"[ObservedPlayer] Identity applied from PlayerList.json: {Name} ({AccountID})");
                    }
                    else
                    {
                        // Fallback: use the nickname stored in the local dogtag database if the
                        // in-game name is not yet available. Don't set _identityApplied so the
                        // real in-game name still takes over as soon as the game provides it.
                        var cached = PlayerLookupApiClient.TryGetCached(ProfileID);
                        if (!string.IsNullOrEmpty(cached?.Nickname))
                        {
                            Name = cached.Nickname;
                            PlayerHistory.AddOrUpdate(this);
                        }
                    }
                }
            }

            // Resolve AccountID from DogtagDatabase once ProfileID is available
            if (string.IsNullOrEmpty(AccountID) && !string.IsNullOrEmpty(ProfileID))
            {
                var cached = PlayerLookupApiClient.TryGetCached(ProfileID);
                if (!string.IsNullOrEmpty(cached?.AccountId))
                {
                    AccountID = cached.AccountId;
                    PlayerHistory.AddOrUpdate(this);
                }
            }

            // Re-check watchlist when AccountID is available (supports mid-raid watchlist additions)
            if (!string.IsNullOrEmpty(AccountID) && IsHumanHostile)
            {
                if (PlayerWatchlist.Entries.TryGetValue(AccountID, out var watchlistEntry))
                {
                    if (Type != PlayerType.SpecialPlayer && Type != PlayerType.Streamer)
                    {
                        Type = PlayerType.SpecialPlayer;
                        UpdateAlerts(watchlistEntry.Reason);

                        if (watchlistEntry.StreamingPlatform != StreamingPlatform.None &&
                            !string.IsNullOrEmpty(watchlistEntry.Username))
                        {
                            StreamingURL = StreamingUtils.GetStreamingURL(
                                watchlistEntry.StreamingPlatform, watchlistEntry.Username);
                            CheckIfStreaming();
                        }
                        else
                        {
                            StreamingURL = null;
                            IsStreaming = false;
                        }
                    }
                }
            }
        }

        private bool _mcSet = false;
        private void UpdateMemberCategory()
        {
            try
            {
                if (!_mcSet)
                {
                    var mcObj = Profile?.MemberCategory;
                    if (mcObj is Enums.EMemberCategory memberCategory)
                    {
                        string alert = null;
                        if ((memberCategory & Enums.EMemberCategory.Developer) == Enums.EMemberCategory.Developer)
                        {
                            alert = "Developer Account";
                            Type = PlayerType.SpecialPlayer;
                        }
                        else if ((memberCategory & Enums.EMemberCategory.Sherpa) == Enums.EMemberCategory.Sherpa)
                        {
                            alert = "Sherpa Account";
                            Type = PlayerType.SpecialPlayer;
                        }
                        else if ((memberCategory & Enums.EMemberCategory.Emissary) == Enums.EMemberCategory.Emissary)
                        {
                            alert = "Emissary Account";
                            Type = PlayerType.SpecialPlayer;
                        }

                        this.UpdateAlerts(alert);

                        _mcSet = true;
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"ERROR updating Member Category for '{Name}': {ex}");
            }
        }

        /// <summary>
        /// Get Player's Updated Health Condition
        /// Only works in Online Mode.
        /// </summary>
        private void UpdateHealthStatus()
        {
            try
            {
                var tag = (Enums.ETagStatus)Memory.ReadValue<int>(ObservedHealthController + Offsets.ObservedHealthController.HealthStatus);
                if ((tag & Enums.ETagStatus.Dying) == Enums.ETagStatus.Dying)
                    HealthStatus = Enums.ETagStatus.Dying;
                else if ((tag & Enums.ETagStatus.BadlyInjured) == Enums.ETagStatus.BadlyInjured)
                    HealthStatus = Enums.ETagStatus.BadlyInjured;
                else if ((tag & Enums.ETagStatus.Injured) == Enums.ETagStatus.Injured)
                    HealthStatus = Enums.ETagStatus.Injured;
                else
                    HealthStatus = Enums.ETagStatus.Healthy;
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"ERROR updating Health Status for '{Name}': {ex}");
            }
        }

        /// <summary>
        /// Get Player's Updated Aiming Status
        /// Only works in Online Mode.
        /// </summary>
        private void UpdateAimingStatus()
        {
            try
            {
                var handsController = Memory.ReadPtr(HandsControllerAddr);
                var bundleAnimBones = Memory.ReadPtr(handsController + Offsets.ObservedHandsController.BundleAnimationBones);
                var pwa = Memory.ReadPtr(bundleAnimBones + Offsets.BundleAnimationBonesController.ProceduralWeaponAnimationObs);
                IsAiming = Memory.ReadValue<bool>(pwa + Offsets.ProceduralWeaponAnimationObs._isAimingObs);
                //if (!IsAI)
                //{
                //    ZoomLevel = GetObservedScopeZoom(pwa);
                //    //XMLogging.WriteLine($"Player '{Name}' Aiming Status: {IsAiming}, ZoomLevel: {ZoomLevel:F2}x");
                //}
                
            }
            catch //(Exception ex)
            {
                //XMLogging.WriteLine($"ERROR updating Aiming Status for '{Name}': {ex}" +
                //    $"\n  HandsControllerAddr : 0x{HandsControllerAddr:X}" +
                //    $"\n  HandsController     : 0x{handsController:X}" +
                //    $"\n  BundleAnimBones     : 0x{bundleAnimBones:X}" +
                //    $"\n  PWA                 : 0x{pwa:X}");
            }
        }

        private static float GetObservedScopeZoom(ulong pwa)
        {
            try
            {
                if (!pwa.IsValidVirtualAddress())
                    return 1f;
                var opticsPtr = Memory.ReadPtr(pwa + 0xF0);
                if (!opticsPtr.IsValidVirtualAddress())
                    return 1f;
                using var optics = MemList<MemPointer>.Get(opticsPtr);
                if (optics.Count <= 0)
                    return 1f;
                var pSightComponent = Memory.ReadPtr(optics[0] + Offsets.SightNBone.Mod);
                if (!pSightComponent.IsValidVirtualAddress())
                    return 1f;
                var sightComponent = Memory.ReadValue<SightComponent>(pSightComponent);
                var zoom = sightComponent.GetZoomLevel();
                return zoom > 1f ? zoom : 1f;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"GetObservedScopeZoom ERROR: {ex}");
                return 1f;
            }
        }

        /// <summary>
        /// Get the Transform Internal Chain for this Player.
        /// </summary>
        /// <param name="bone">Bone to lookup.</param>
        /// <returns>Array of offsets for transform internal chain.</returns>
        public override uint[] GetTransformInternalChain(Bones bone) => Offsets.ObservedPlayerView.GetTransformChain(bone);

        #region SightComponent structures

        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        private readonly ref struct SightComponent // EFT.InventoryLogic.SightComponent
        {
            [FieldOffset((int)Offsets.SightComponent._template)]
            private readonly ulong pSightInterface;

            [FieldOffset((int)Offsets.SightComponent.ScopesSelectedModes)]
            private readonly ulong pScopeSelectedModes;

            [FieldOffset((int)Offsets.SightComponent.SelectedScope)]
            private readonly int SelectedScope;

            [FieldOffset((int)Offsets.SightComponent.ScopeZoomValue)]
            public readonly float ScopeZoomValue;

            public readonly float GetZoomLevel()
            {
                using var zoomArray = SightInterface.Zooms;

                if (SelectedScope >= zoomArray.Count || SelectedScope is < 0 or > 10)
                    return -1.0f;

                using var selectedScopeModes = MemArray<int>.Get(pScopeSelectedModes, false);
                int selectedScopeMode = SelectedScope >= selectedScopeModes.Count ? 0 : selectedScopeModes[SelectedScope];
                ulong zoomAddr = zoomArray[SelectedScope] + MemArray<float>.ArrBaseOffset + (uint)selectedScopeMode * 0x4;

                float zoomLevel = Memory.ReadValue<float>(zoomAddr, false);

                if (zoomLevel.IsNormalOrZero() && zoomLevel is >= 0f and < 100f)
                    return zoomLevel;

                return -1.0f;
            }

            public readonly SightInterface SightInterface =>
                Memory.ReadValue<SightInterface>(pSightInterface);
        }

        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        private readonly ref struct SightInterface // -.GInterfaceBB26
        {
            [FieldOffset((int)Offsets.SightInterface.Zooms)]
            private readonly ulong pZooms;

            public readonly MemArray<ulong> Zooms =>
                MemArray<ulong>.Get(pZooms);
        }

        #endregion

    }
}
