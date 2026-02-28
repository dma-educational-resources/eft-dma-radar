using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Misc.Pools;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.Collections;

namespace eft_dma_radar.Tarkov.EFTPlayer.Plugins
{
    public sealed class FirearmManager
    {
        /// <summary>
        /// Program Configuration.
        /// </summary>
        private static Config Config => Program.Config;

        private readonly LocalPlayer _localPlayer;
        private CachedHandsInfo _hands;

        /// <summary>
        /// Returns the Hands Controller Address and if the held item is a weapon.
        /// </summary>
        public Tuple<ulong, bool> HandsController => new(_hands, _hands?.IsWeapon ?? false);

        /// <summary>
        /// Magazine (if any) contained in this firearm.
        /// </summary>
        public MagazineManager Magazine { get; private set; }
        /// <summary>
        /// Current Firearm Fireport Transform.
        /// </summary>
        public UnityTransform FireportTransform { get; private set; }
        /// <summary>
        /// Last known Fireport Position.
        /// </summary>
        public Vector3? FireportPosition { get; private set; }
        /// <summary>
        /// Last known Fireport Rotation.
        /// </summary>
        public Quaternion? FireportRotation { get; private set; }

        public FirearmManager(LocalPlayer localPlayer)
        {
            _localPlayer = localPlayer;
            Magazine = new(localPlayer);
        }

        /// <summary>
        /// Realtime Loop for FirearmManager chained from LocalPlayer.
        /// </summary>
        /// <param name="index"></param>
        public void OnRealtimeLoop(ScatterReadIndex index)
        {
            var fireport = FireportTransform;
            if (fireport == null)
                return;

            // HARD VALIDATION ¡ª prevents poison scatter entries
            if (!fireport.VerticesAddr.IsValidVirtualAddress() ||
                fireport.Index < 0 ||
                fireport.Index > 128)
            {
                ResetFireport();
                return;
            }

            index.AddEntry<SharedArray<UnityTransform.TrsX>>(
                -20,
                fireport.VerticesAddr,
                (fireport.Index + 1) * SizeChecker<UnityTransform.TrsX>.Size);

            index.Callbacks += x1 =>
            {
                if (x1.TryGetResult<SharedArray<UnityTransform.TrsX>>(-20, out var vertices))
                    UpdateFireport(vertices);
                else
                {
                    _fireportStallTicks++;
                    if (_fireportStallTicks > 15)
                        ResetFireport();
                }
            };
        }
        private long _nextFireportRetry;

        private bool CanRetryFireport()
        {
            long now = Environment.TickCount64;
            if (now < _nextFireportRetry)
                return false;

            _nextFireportRetry = now + 30; // retry every 250ms
            return true;
        }
        /// <summary>
        /// Update Hands/Firearm/Magazine information for LocalPlayer.
        /// </summary>
        public void Update()
        {
            try
            {
                var hands = ILocalPlayer.HandsController;
                if (!hands.IsValidVirtualAddress())
                    return;
                if (hands != _hands)
                {
                    _hands = null;
                    ResetFireport();
                    Magazine = new(_localPlayer);
                    _hands = GetHandsInfo(hands);
                }
                if (_hands.IsWeapon)
                {
                    if (CameraManagerBase.EspRunning && Config.ESP.ShowMagazine)
                    {
                        try
                        {
                            Magazine.Update(_hands);
                        }
                        catch
                        {
                            Magazine = new(_localPlayer);
                        }
                    }
                    if (FireportTransform is UnityTransform fireportTransform) // Validate Fireport Transform
                    {
                        try
                        {
                            var v = Memory.ReadPtrChain(hands, Offsets.FirearmController.To_FirePortVertices, false);
                            if (fireportTransform.VerticesAddr != v)
                                ResetFireport();
                        }
                        catch
                        {
                            ResetFireport(); // Silently reset - expected during weapon swap
                        }
                    }
                    if (FireportTransform is null && CanRetryFireport())
                    {
                        try
                        {
                            var t = Memory.ReadPtrChain(hands,
                                Offsets.FirearmController.To_FirePortTransformInternal, false);

                            if (!t.IsValidVirtualAddress())
                                return;

                            FireportTransform = new UnityTransform(t, false);

                            var pos = FireportTransform.UpdatePosition();
                            if (Vector3.Distance(pos, _localPlayer.Position) > 100f)
                            {
                                ResetFireport();
                                return;
                            }

                            FireportPosition = pos;
                            FireportRotation = FireportTransform.GetRotation();
                        }
                        catch
                        {
                            ResetFireport();
                        }
                    }
                    else
                    {
                        // FAST PATH ¡ª direct read for immediate visual update
                        try
                        {
                            var pos = FireportTransform.UpdatePosition();
                            var rot = FireportTransform.GetRotation();

                            // Accept sane positions immediately
                            if (Vector3.Distance(pos, _localPlayer.Position) <= 100f)
                            {
                                FireportPosition = pos;
                                FireportRotation = rot;
                            }
                            else
                            {
                                ResetFireport();
                                return;
                            }
                        }
                        catch
                        {
                            // ignore ¡ª scatter may still succeed
                        }
                    }
                }
            }
            catch
            {
                // Silently handle - will retry next frame
            }
        }

        /// <summary>
        /// Update cached fireport position/rotation (called from Main Loop).
        /// </summary>
        /// <param name="vertices">Fireport transform vertices.</param>
        private int _fireportStallTicks;
        
        private void UpdateFireport(SharedArray<UnityTransform.TrsX> vertices)
        {
            try
            {
                var pos = FireportTransform?.UpdatePosition(vertices);
        
                if (pos == FireportPosition)
                    _fireportStallTicks++;
                else
                    _fireportStallTicks = 0;
        
                if (_fireportStallTicks > 30)
                {
                    ResetFireport();
                    return;
                }
        
                FireportPosition = pos;
                FireportRotation = FireportTransform?.GetRotation(vertices);
            }
            catch
            {
                ResetFireport();
            }
        }


        /// <summary>
        /// Reset the Fireport Data.
        /// </summary>
        private void ResetFireport()
        {
            FireportTransform = null;
            FireportPosition = null;
            FireportRotation = null;
        }

        /// <summary>
        /// Get updated hands information.
        /// </summary>
        private static CachedHandsInfo GetHandsInfo(ulong handsController)
        {
            var itemBase = Memory.ReadPtr(handsController + Offsets.ItemHandsController.Item, false);
            var itemTemp = Memory.ReadPtr(itemBase + Offsets.LootItem.Template, false);
            var itemIdPtr = Memory.ReadValue<Types.MongoID>(itemTemp + Offsets.ItemTemplate._id, false);
            var itemId = Memory.ReadUnityString(itemIdPtr.StringID, 64, false);
            ArgumentOutOfRangeException.ThrowIfNotEqual(itemId.Length, 24, nameof(itemId));
            if (!EftDataManager.AllItems.TryGetValue(itemId, out var heldItem))
                return new(handsController);
            return new(handsController, heldItem, itemBase);
        }

        #region Magazine Info

        /// <summary>
        /// Helper class to track a Player's Magazine Ammo Count.
        /// </summary>
        public sealed class MagazineManager
        {
            private readonly LocalPlayer _localPlayer;
            private string _fireType;
            private string _ammo;

            /// <summary>
            /// True if the MagazineManager is in a valid state for data output.
            /// </summary>
            public bool IsValid => MaxCount > 0;
            /// <summary>
            /// Current ammo count in Magazine.
            /// </summary>
            public int Count { get; private set; }
            /// <summary>
            /// Maximum ammo count in Magazine.
            /// </summary>
            public int MaxCount { get; private set; }
            /// <summary>
            /// Weapon Fire Mode & Ammo Type in a formatted string.
            /// </summary>
            public string WeaponInfo
            {
                get
                {
                    string result = "";
                    string ft = _fireType;
                    string ammo = _ammo;
                    if (ft is not null)
                        result += $"{ft}: ";
                    if (ammo is not null)
                        result += ammo;
                    if (string.IsNullOrEmpty(result))
                        return null;
                    return result.Trim().TrimEnd(':');
                }
            }

            /// <summary>
            /// Constructor.
            /// </summary>
            /// <param name="player">Player to track magazine usage for.</param>
            public MagazineManager(LocalPlayer localPlayer)
            {
                _localPlayer = localPlayer;
            }

            /// <summary>
            /// Update Magazine Information for this instance.
            /// </summary>
            public void Update(CachedHandsInfo hands)
            {
                string ammoInChamber = null;
                string fireType = null;
                string ammoFromMag = null;
                int maxCount = 0;
                int currentCount = 0;
                var fireModePtr = Memory.ReadValue<ulong>(hands.ItemAddr + Offsets.LootItemWeapon.FireMode);
                var magSlotPtr = Memory.ReadValue<ulong>(hands.ItemAddr + Offsets.LootItemWeapon._magSlotCache);
                if (fireModePtr != 0x0)
                {
                    var fireMode = (EFireMode)Memory.ReadValue<byte>(fireModePtr + Offsets.FireModeComponent.FireMode);
                    if (fireMode >= EFireMode.Auto && fireMode <= EFireMode.SemiAuto)
                        fireType = fireMode.GetDescription();
                }

                try // does nothing, only exists for the catch to happen
                {
                    var chambers = Memory.ReadPtr(hands.ItemAddr + Offsets.LootItemWeapon.Chambers);
                    var slotPtr = Memory.ReadPtr(chambers + MemList<byte>.ArrStartOffset + 0 * 0x8); // One in the chamber ;)
                    var slotItem = Memory.ReadPtr(slotPtr + Offsets.Slot.ContainedItem);
                    var ammoTemplate = Memory.ReadPtr(slotItem + Offsets.LootItem.Template);
                    var idPtr = Memory.ReadValue<Types.MongoID>(ammoTemplate + Offsets.ItemTemplate._id);
                    string id = Memory.ReadUnityString(idPtr.StringID);
                    if (EftDataManager.AllItems.TryGetValue(id, out var ammo))
                        ammoInChamber = ammo?.ShortName;
                }
                catch
                {
                    var ammoTemplate_ = GetAmmoTemplateFromWeapon(hands.ItemAddr);
                    var ammoIdPtr = Memory.ReadValue<Types.MongoID>(ammoTemplate_ + Offsets.ItemTemplate._id);
                    string ammoId = Memory.ReadUnityString(ammoIdPtr.StringID);
                    if (EftDataManager.AllItems.TryGetValue(ammoId, out var ammo))
                        ammoFromMag = ammo?.ShortName;
                    var magItemPtr = Memory.ReadPtr(magSlotPtr + Offsets.Slot.ContainedItem);
                    var cartridges = Memory.ReadPtr(magItemPtr + Offsets.LootItemMagazine.Cartridges);
                    var magStackPtr = Memory.ReadPtr(cartridges + Offsets.StackSlot._items);
                    var magStack = MemList<ulong>.Get(magStackPtr);
                    maxCount += Memory.ReadValue<int>(cartridges + Offsets.StackSlot.MaxCount);
                    var magStackPtr_ = Memory.ReadPtr(cartridges + Offsets.StackSlot._items);
                    using var magStack_ = MemList<ulong>.Get(magStackPtr);
                    foreach (var stack in magStack_) // Each ammo type will be a separate stack
                    {
                        if (stack != 0x0)
                            currentCount += Memory.ReadValue<int>(stack + Offsets.MagazineClass.StackObjectsCount, false);
                    }
                }

                var chambersPtr = Memory.ReadValue<ulong>(hands.ItemAddr + Offsets.LootItemWeapon.Chambers);
                if (chambersPtr != 0x0) // Single chamber, or for some shotguns, multiple chambers
                {
                    using var chambers = MemArray<Chamber>.Get(chambersPtr);
                    currentCount += chambers.Count(x => x.HasBullet());
                    ammoInChamber = GetLoadedAmmoName(chambers.FirstOrDefault(x => x.HasBullet()));
                    maxCount += chambers.Count;
                }

                if (magSlotPtr != 0x0)
                {
                    var magItem = Memory.ReadValue<ulong>(magSlotPtr + Offsets.Slot.ContainedItem);
                    if (magItem != 0x0)
                    {
                        var magChambersPtr = Memory.ReadPtr(magItem + Offsets.LootItemMod.Slots);
                        using var magChambers = MemArray<Chamber>.Get(magChambersPtr);
                        if (magChambers.Count > 0 || ammoInChamber is null) // Revolvers, etc.
                        {
                            maxCount += magChambers.Count;
                            currentCount += magChambers.Count(x => x.HasBullet());
                            ammoInChamber = GetLoadedAmmoName(magChambers.FirstOrDefault(x => x.HasBullet()));
                        }
                        else // Regular magazines
                        {
                            var cartridges = Memory.ReadPtr(magItem + Offsets.LootItemMagazine.Cartridges);
                            maxCount += Memory.ReadValue<int>(cartridges + Offsets.StackSlot.MaxCount);
                            var magStackPtr = Memory.ReadPtr(cartridges + Offsets.StackSlot._items);
                            using var magStack = MemList<ulong>.Get(magStackPtr);
                            foreach (var stack in magStack) // Each ammo type will be a separate stack
                            {
                                if (stack != 0x0)
                                    currentCount += Memory.ReadValue<int>(stack + Offsets.MagazineClass.StackObjectsCount, false);
                            }
                        }
                    }
                }

                _ammo = ammoInChamber ?? ammoFromMag;
                _fireType = fireType;
                Count = currentCount;
                MaxCount = maxCount;
            }

            /// <summary>
            /// Gets the name of the ammo round currently loaded in this chamber, otherwise NULL.
            /// </summary>
            /// <param name="chamber">Chamber to check.</param>
            /// <returns>Short name of ammo in chamber, or null if no round loaded.</returns>
            private static string GetLoadedAmmoName(Chamber chamber)
            {
                if (chamber != 0x0)
                {
                    var bulletItem = Memory.ReadValue<ulong>(chamber + Offsets.Slot.ContainedItem);
                    if (bulletItem != 0x0)
                    {
                        var bulletTemp = Memory.ReadPtr(bulletItem + Offsets.LootItem.Template);
                        var bulletIdPtr = Memory.ReadValue<Types.MongoID>(bulletTemp + Offsets.ItemTemplate._id);
                        var bulletId = Memory.ReadUnityString(bulletIdPtr.StringID, 32);
                        if (EftDataManager.AllItems.TryGetValue(bulletId, out var bullet))
                            return bullet?.ShortName;
                    }
                }
                return null;
            }

            /// <summary>
            /// Returns the Ammo Template from a Weapon (First loaded round).
            /// </summary>
            /// <param name="lootItemBase">EFT.InventoryLogic.Weapon instance</param>
            /// <returns>Ammo Template Ptr</returns>
            public static ulong GetAmmoTemplateFromWeapon(ulong lootItemBase)
            {
                var chambersPtr = Memory.ReadValue<ulong>(lootItemBase + Offsets.LootItemWeapon.Chambers);
                ulong firstRound;
                MemArray<Chamber> chambers = null;
                MemArray<Chamber> magChambers = null;
                MemList<ulong> magStack = null;
                try
                {
                    if (chambersPtr != 0x0 && (chambers = MemArray<Chamber>.Get(chambersPtr)).Count > 0) // Single chamber, or for some shotguns, multiple chambers
                        firstRound = Memory.ReadPtr(chambers.First(x => x.HasBullet(true)) + Offsets.Slot.ContainedItem);
                    else
                    {
                        var magSlot = Memory.ReadPtr(lootItemBase + Offsets.LootItemWeapon._magSlotCache);
                        var magItemPtr = Memory.ReadPtr(magSlot + Offsets.Slot.ContainedItem);
                        var magChambersPtr = Memory.ReadPtr(magItemPtr + Offsets.LootItemMod.Slots);
                        magChambers = MemArray<Chamber>.Get(magChambersPtr);
                        if (magChambers.Count > 0) // Revolvers, etc.
                            firstRound = Memory.ReadPtr(magChambers.First(x => x.HasBullet(true)) + Offsets.Slot.ContainedItem);
                        else // Regular magazines
                        {
                            var cartridges = Memory.ReadPtr(magItemPtr + Offsets.LootItemMagazine.Cartridges);
                            var magStackPtr = Memory.ReadPtr(cartridges + Offsets.StackSlot._items);
                            magStack = MemList<ulong>.Get(magStackPtr);
                            firstRound = magStack[0];
                        }
                    }
                    return Memory.ReadPtr(firstRound + Offsets.LootItem.Template);
                }
                finally
                {
                    chambers?.Dispose();
                    magChambers?.Dispose();
                    magStack?.Dispose();
                }
            }

            /// <summary>
            /// Wrapper defining a Chamber Structure.
            /// </summary>
            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            private readonly struct Chamber
            {
                public static implicit operator ulong(Chamber x) => x._base;
                private readonly ulong _base;

                public readonly bool HasBullet(bool useCache = false)
                {
                    if (_base == 0x0)
                        return false;
                    return Memory.ReadValue<ulong>(_base + Offsets.Slot.ContainedItem, useCache) != 0x0;
                }
            }

            private enum EFireMode : byte
            {
                // Token: 0x0400B0EE RID: 45294
                [Description(nameof(Auto))]
                Auto = 0,
                // Token: 0x0400B0EF RID: 45295
                [Description(nameof(Single))]
                Single = 1,
                // Token: 0x0400B0F0 RID: 45296
                [Description(nameof(DbTap))]
                DbTap = 2,
                // Token: 0x0400B0F1 RID: 45297
                [Description(nameof(Burst))]
                Burst = 3,
                // Token: 0x0400B0F2 RID: 45298
                [Description(nameof(DbAction))]
                DbAction = 4,
                // Token: 0x0400B0F3 RID: 45299
                [Description(nameof(SemiAuto))]
                SemiAuto = 5
            }
        }

        #endregion

        #region Hands Cache

        public sealed class CachedHandsInfo
        {
            public static implicit operator ulong(CachedHandsInfo x) => x?._hands ?? 0x0;

            private readonly ulong _hands;
            private readonly TarkovMarketItem _item;
            /// <summary>
            /// Address of currently held item (if any).
            /// </summary>
            public ulong ItemAddr { get; }
            /// <summary>
            /// True if the Item being currently held (if any) is a weapon, otherwise False.
            /// </summary>
            public bool IsWeapon => _item?.IsWeapon ?? false;

            public CachedHandsInfo(ulong handsController)
            {
                _hands = handsController;
            }

            public CachedHandsInfo(ulong handsController, TarkovMarketItem item, ulong itemAddr)
            {
                _hands = handsController;
                _item = item;
                ItemAddr = itemAddr;
            }
        }

        #endregion
    }
}