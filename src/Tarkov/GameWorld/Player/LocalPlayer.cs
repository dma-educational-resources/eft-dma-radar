#pragma warning disable IDE0130
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.Unity;
using eft_dma_radar.Tarkov.Unity.Collections;
using eft_dma_radar.Tarkov.GameWorld.Explosives;
using eft_dma_radar.DMA.ScatterAPI;

namespace eft_dma_radar.Tarkov.EFTPlayer
{
    public sealed class LocalPlayer : ClientPlayer, ILocalPlayer
    {
        /// <summary>
        /// All Items on the Player's WishList.
        /// </summary>
        public static IReadOnlySet<string> WishlistItems => _wishlistItems;
        private static HashSet<string> _wishlistItems = new(StringComparer.OrdinalIgnoreCase);

        private ulong _healthController = 0;
        private ulong _energyPtr = 0;
        private ulong _hydrationPtr = 0;

        private float _cachedEnergy = 0f;
        private float _cachedHydration = 0f;
        private RateLimiter _energyHydrationRefreshLimit = new(TimeSpan.FromSeconds(3));
        private RateLimiter _energyHydrationErrLimit = new(TimeSpan.FromSeconds(30));
        private Action<ScatterReadIndex> _localRealtimeCallback;

        /// <summary>
        /// ValueStruct layout for reading Current/Maximum health values (IL2CPP).
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        private struct ValueStruct
        {
            [FieldOffset(0x0)]
            public float Current;
            [FieldOffset(0x4)]
            public float Maximum;
        }

        /// <summary>
        /// Spawn Point.
        /// </summary>
        public string EntryPoint { get; }
        /// <summary>
        /// Profile ID (if Player Scav).
        /// Used for Exfils.
        /// </summary>
        public string ProfileId { get; }
        /// <summary>
        /// Firearm Information.
        /// </summary>
        public FirearmManager Firearm { get; }
        public ExplosivesManager explosives { get; }
        /// <summary>
        /// Player name.
        /// </summary>
        public override string Name
        {
            get => "localPlayer";
            set { }
        }
        /// <summary>
        /// Player is Human-Controlled.
        /// </summary>
        public override bool IsHuman { get; }

        public LocalPlayer(ulong playerBase) : base(playerBase)
        {
            if (!ObjectClass.TryReadName(this, out var classType))
                throw new ArgumentOutOfRangeException(nameof(classType));
            if (!(classType == "LocalPlayer" || classType == "ClientPlayer"))
                throw new ArgumentOutOfRangeException(nameof(classType));

            IsHuman = true;

            // IL2CPP: Simplified initialization (following XM's pattern)
            this.Firearm = new(this);

            // Read entry point/profile ID lazily if needed
            if (IsPmc)
            {
                if (Memory.TryReadPtr(Info + Offsets.PlayerInfo.EntryPoint, out var entryPtr)
                    && Memory.TryReadUnityString(entryPtr, out var entryPoint))
                    EntryPoint = entryPoint;
            }
            else if (IsScav)
            {
                if (Memory.TryReadPtr(this.Profile + Offsets.Profile.Id, out var profileIdPtr)
                    && Memory.TryReadUnityString(profileIdPtr, out var profileId))
                    ProfileId = profileId;
            }

            // IL2CPP: HealthController moved - initialize lazily when needed
            // (accessing _healthController at old offset 0x940 reads floats, not pointers)
            try
            {
                InitializeHealthPointers();
            }
            catch
            {
                // Health info will be unavailable but player will still work
            }
        }

        /// <summary>
        /// Initialize health controller and energy/hydration pointers (IL2CPP).
        /// </summary>
        private void InitializeHealthPointers()
        {
            try
            {
                if (!Memory.TryReadPtr(Base + Offsets.Player._healthController, out var healthController, false))
                {
                    Log.Write(AppLogLevel.Warning, "Failed to read HealthController pointer", "LocalPlayer");
                    return;
                }
                _healthController = healthController;

                if (!_healthController.IsValidVirtualAddress())
                {
                    Log.Write(AppLogLevel.Warning, "HealthController address invalid", "LocalPlayer");
                    return;
                }

                Memory.TryReadPtr(_healthController + Offsets.HealthController.Energy, out _energyPtr, false);
                Memory.TryReadPtr(_healthController + Offsets.HealthController.Hydration, out _hydrationPtr, false);

                if (_energyPtr.IsValidVirtualAddress() && _hydrationPtr.IsValidVirtualAddress())
                {
                    Log.Write(AppLogLevel.Debug, $"Health pointers initialized: Energy=0x{_energyPtr:X}, Hydration=0x{_hydrationPtr:X}", "LocalPlayer");
                }
                else
                {
                    Log.Write(AppLogLevel.Warning, "Energy/Hydration pointers invalid", "LocalPlayer");
                }
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Error, $"Failed to initialize health pointers: {ex.Message}", "LocalPlayer");
            }
        }

        /// <summary>
        /// Set the Player's WishList.
        /// </summary>
        public void RefreshWishlist()
        {
            if (!Memory.TryReadPtr(this.Profile + Offsets.Profile.WishlistManager, out var wishlistManager))
                return;
            if (!Memory.TryReadPtr(wishlistManager + Offsets.WishlistManager.Items, out var itemsPtr))
                return;
            using var items = MemDictionary<Types.MongoID, int>.Get(itemsPtr);
            var wishlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                try
                {
                    if (item.Key.StringID == 0)
                        continue;
                    if (!Memory.TryReadUnityString(item.Key.StringID, out var id) || string.IsNullOrWhiteSpace(id))
                        continue;
                    wishlist.Add(id);
                }
                catch { }
            }
            _wishlistItems = wishlist;
        }

        /// <summary>
        /// Additional realtime reads for LocalPlayer.
        /// </summary>
        /// <param name="index"></param>
        public override void OnRealtimeLoop(ScatterReadIndex index)
        {
            index.AddEntry<MemPointer>(-10, this.MovementContext + Offsets.MovementContext.CurrentState);
            index.AddEntry<MemPointer>(-11, this.HandsControllerAddr);
            _localRealtimeCallback ??= LocalRealtimeCallback;
            index.Callbacks += _localRealtimeCallback;
            Firearm.OnRealtimeLoop(index);
            //explosives.OnRealtimeLoop(_scatterIndex);
            base.OnRealtimeLoop(index);
        }

        private void LocalRealtimeCallback(ScatterReadIndex x1)
        {
            if (x1.TryGetResult<MemPointer>(-10, out var currentState))
                ILocalPlayer.PlayerState = currentState;
            if (x1.TryGetResult<MemPointer>(-11, out var handsController))
                ILocalPlayer.HandsController = handsController;
        }

        /// <summary>
        /// Get View Angles for LocalPlayer.
        /// </summary>
        /// <returns>View Angles (Vector2).</returns>
        public Vector2 GetViewAngles() =>
            Memory.ReadValue<Vector2>(this.RotationAddress, false);

        /// <summary>
        /// Checks if LocalPlayer is Aiming (ADS).
        /// </summary>
        /// <returns>True if aiming (ADS), otherwise False.</returns>
        public bool CheckIfADS()
        {
            try
            {
                if (!Memory.TryReadValue<bool>(this.PWA + Offsets.ProceduralWeaponAnimation._isAiming, out var isAiming, false))
                    return false;
                return isAiming;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"CheckIfADS() ERROR: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Gets the current energy level (cached, updated every 3 seconds).
        /// </summary>
        /// <returns>Energy level as a float.</returns>
        public float GetEnergy()
        {
            try
            {
                if (_energyPtr == 0)
                    return 100f; // Default if not available

                if (_energyHydrationRefreshLimit.TryEnter())
                    UpdateEnergyHydrationCache();

                return _cachedEnergy;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"GetEnergy() ERROR: {ex}");
                return _cachedEnergy;
            }
        }

        /// <summary>
        /// Gets the current hydration level (cached, updated every 3 seconds).
        /// </summary>
        /// <returns>Hydration level as a float.</returns>
        public float GetHydration()
        {
            try
            {
                if (_hydrationPtr == 0)
                    return 100f; // Default if not available

                if (_energyHydrationRefreshLimit.TryEnter())
                    UpdateEnergyHydrationCache();

                return _cachedHydration;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"GetHydration() ERROR: {ex}");
                return _cachedHydration;
            }
        }

        /// <summary>
        /// Updates the cached energy and hydration values from memory (IL2CPP).
        /// </summary>
        private void UpdateEnergyHydrationCache()
        {
            try
            {
                if (!_energyPtr.IsValidVirtualAddress() || !_hydrationPtr.IsValidVirtualAddress())
                {
                    // Try to re-initialize pointers
                    InitializeHealthPointers();
                    if (!_energyPtr.IsValidVirtualAddress() || !_hydrationPtr.IsValidVirtualAddress())
                        return;
                }

                // Read ValueStruct from HealthValue.Value (offset 0x10)
                if (Memory.TryReadValue<ValueStruct>(_energyPtr + Offsets.HealthValue.Value, out var energyStruct, false))
                    _cachedEnergy = energyStruct.Current;
                if (Memory.TryReadValue<ValueStruct>(_hydrationPtr + Offsets.HealthValue.Value, out var hydrationStruct, false))
                    _cachedHydration = hydrationStruct.Current;
            }
            catch (Exception ex)
            {
                // Rate limit this error message
                if (_energyHydrationErrLimit.TryEnter())
                    Log.Write(AppLogLevel.Error,
                        $"UpdateEnergyHydrationCache failed: {ex.Message}",
                        "LocalPlayer");
            }
        }
    }
}
