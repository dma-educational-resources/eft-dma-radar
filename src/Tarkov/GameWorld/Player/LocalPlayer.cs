using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;

using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.Collections;
using System.Runtime.InteropServices;
using eft_dma_radar.Tarkov.GameWorld.Explosives;

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
        private DateTime _lastEnergyHydrationRead = DateTime.MinValue;
        private readonly TimeSpan _energyHydrationCacheTime = TimeSpan.FromSeconds(3);

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
            string classType = ObjectClass.ReadName(this);
            if (!(classType == "LocalPlayer" || classType == "ClientPlayer"))
                throw new ArgumentOutOfRangeException(nameof(classType));

            IsHuman = true;

            // IL2CPP: Simplified initialization (following XM's pattern)
            this.Firearm = new(this);

            // Read entry point/profile ID lazily if needed
            if (IsPmc)
            {
                try
            {
                var entryPtr = Memory.ReadPtr(Info + Offsets.PlayerInfo.EntryPoint);
                EntryPoint = Memory.ReadUnityString(entryPtr);
                }
                catch { }
            }
            else if (IsScav)
            {
                try
            {
                var profileIdPtr = Memory.ReadPtr(this.Profile + Offsets.Profile.Id);
                ProfileId = Memory.ReadUnityString(profileIdPtr);
            }
                catch { }
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

            try
            {
            ulong id = ulong.Parse(AccountID);
            ILocalPlayer.AccountId = id;
            }
            catch { }
        }

        /// <summary>
        /// Initialize health controller and energy/hydration pointers (IL2CPP).
        /// </summary>
        private void InitializeHealthPointers()
        {
            try
            {
                _healthController = Memory.ReadPtr(Base + Offsets.Player._healthController, false);
                if (!_healthController.IsValidVirtualAddress())
                {
                    XMLogging.WriteLine("[LocalPlayer] HealthController address invalid");
                    return;
                }

                _energyPtr = Memory.ReadPtr(_healthController + Offsets.HealthController.Energy, false);
                _hydrationPtr = Memory.ReadPtr(_healthController + Offsets.HealthController.Hydration, false);
                
                if (_energyPtr.IsValidVirtualAddress() && _hydrationPtr.IsValidVirtualAddress())
                {
                    XMLogging.WriteLine($"[LocalPlayer] Health pointers initialized: Energy=0x{_energyPtr:X}, Hydration=0x{_hydrationPtr:X}");
                }
                else
                {
                    XMLogging.WriteLine("[LocalPlayer] Energy/Hydration pointers invalid");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[LocalPlayer] Failed to initialize health pointers: {ex.Message}");
            }
        }

        /// <summary>
        /// Set the Player's WishList.
        /// </summary>
        public void RefreshWishlist()
        {
            var wishlistManager = Memory.ReadPtr(this.Profile + Offsets.Profile.WishlistManager);
            var itemsPtr = Memory.ReadPtr(wishlistManager + Offsets.WishlistManager.Items);
            using var items = MemDictionary<Types.MongoID, int>.Get(itemsPtr);
            var wishlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                try
                {
                    if (item.Key.StringID == 0)
                        continue;
                    string id = Memory.ReadUnityString(item.Key.StringID);
                    if (string.IsNullOrWhiteSpace(id))
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
            index.Callbacks += x1 =>
            {
                if (x1.TryGetResult<MemPointer>(-10, out var currentState))
                    ILocalPlayer.PlayerState = currentState;
                if (x1.TryGetResult<MemPointer>(-11, out var handsController))
                    ILocalPlayer.HandsController = handsController;
            };
            Firearm.OnRealtimeLoop(index);
            //explosives.OnRealtimeLoop(_scatterIndex);
            base.OnRealtimeLoop(index);
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
                return Memory.ReadValue<bool>(this.PWA + Offsets.ProceduralWeaponAnimation._isAiming, false);
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"CheckIfADS() ERROR: {ex}");
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

                if (DateTime.UtcNow - _lastEnergyHydrationRead >= _energyHydrationCacheTime)
                    UpdateEnergyHydrationCache();

                return _cachedEnergy;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"GetEnergy() ERROR: {ex}");
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

                if (DateTime.UtcNow - _lastEnergyHydrationRead >= _energyHydrationCacheTime)
                    UpdateEnergyHydrationCache();

                return _cachedHydration;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"GetHydration() ERROR: {ex}");
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
                var energyStruct = Memory.ReadValue<ValueStruct>(_energyPtr + Offsets.HealthValue.Value, false);
                var hydrationStruct = Memory.ReadValue<ValueStruct>(_hydrationPtr + Offsets.HealthValue.Value, false);

                _cachedEnergy = energyStruct.Current;
                _cachedHydration = hydrationStruct.Current;
                _lastEnergyHydrationRead = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[LocalPlayer] UpdateEnergyHydrationCache failed: {ex.Message}");
            }
        }
    }
}
