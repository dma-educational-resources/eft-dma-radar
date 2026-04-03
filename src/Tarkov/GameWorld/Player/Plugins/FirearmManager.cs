#pragma warning disable IDE0130
using eft_dma_radar.DMA.ScatterAPI;
using eft_dma_radar.Misc.Data;
using eft_dma_radar.Misc.Pools;
using eft_dma_radar.Tarkov.Unity;
using eft_dma_radar.Tarkov.Unity.Collections;
using eft_dma_radar.UI.Misc;

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
        private Action<ScatterReadIndex> _fireportCallback;

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

            _fireportCallback ??= FireportRealtimeCallback;
            index.Callbacks += _fireportCallback;
        }

        private void FireportRealtimeCallback(ScatterReadIndex x1)
        {
            if (x1.TryGetResult<SharedArray<UnityTransform.TrsX>>(-20, out var vertices))
                UpdateFireport(vertices);
            else
            {
                _fireportStallTicks++;
                if (_fireportStallTicks > 15)
                    ResetFireport();
            }
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
                            if (FireportTransform is null)
                            {
                                ResetFireport();
                                return;
                            }
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

            internal static bool DebugLogging = false;

            private string _lastValidAmmo;
            private string _lastValidFireType;
            private int _lastValidCount;
            private int _lastValidMaxCount;

            /// <summary>
            /// True if the MagazineManager is in a valid state for data output.
            /// </summary>
            public bool IsValid => MaxCount > 0 || _lastValidMaxCount > 0;
            /// <summary>
            /// Current ammo count in Magazine.
            /// </summary>
            public int Count { get; private set; }
            /// <summary>
            /// Maximum ammo count in Magazine.
            /// </summary>
            public int MaxCount { get; private set; }
            /// <summary>
            /// Current ammo count, falling back to last valid reading if current is zero.
            /// </summary>
            public int CountWithFallback => Count > 0 ? Count : _lastValidCount;
            /// <summary>
            /// Maximum ammo count, falling back to last valid reading if current is zero.
            /// </summary>
            public int MaxCountWithFallback => MaxCount > 0 ? MaxCount : _lastValidMaxCount;
            /// <summary>
            /// Weapon Fire Mode & Ammo Type in a formatted string.
            /// </summary>
            public string WeaponInfo
            {
                get
                {
                    string result = "";
                    string ft = _fireType ?? _lastValidFireType;
                    string ammo = _ammo ?? _lastValidAmmo;
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
                bool log = DebugLogging &&
                            Log.TryThrottle("MagCheck", TimeSpan.FromSeconds(1));

                string ammoInChamber = null;
                string fireType = null;
                string ammoFromMag = null;
                int maxCount = 0;
                int currentCount = 0;
                int chamberSlotCount = 0;

                var fireModePtr = Memory.ReadValue<ulong>(hands.ItemAddr + Offsets.LootItemWeapon.FireMode);
                var magSlotPtr = Memory.ReadValue<ulong>(hands.ItemAddr + Offsets.LootItemWeapon._magSlotCache);

                if (log)
                {
                    string weaponClass    = ObjectClass.ReadName(hands.ItemAddr, useCache: false);
                    string fireModeClass  = fireModePtr != 0 ? ObjectClass.ReadName(fireModePtr, useCache: false) : "null";
                    string magSlotClass   = magSlotPtr  != 0 ? ObjectClass.ReadName(magSlotPtr,  useCache: false) : "null";
                    Log.WriteLine($"[MagCheck] " + $"-- WeaponBase: 0x{hands.ItemAddr:X16}  class={weaponClass}");
                    Log.WriteLine($"[MagCheck] " + $"  FireMode   : 0x{hands.ItemAddr:X16} +0x{Offsets.LootItemWeapon.FireMode:X3} ? ptr=0x{fireModePtr:X16}  class={fireModeClass}");
                    Log.WriteLine($"[MagCheck] " + $"  MagSlot    : 0x{hands.ItemAddr:X16} +0x{Offsets.LootItemWeapon._magSlotCache:X3} ? ptr=0x{magSlotPtr:X16}  class={magSlotClass}");
                }

                if (fireModePtr != 0x0)
                {
                    var fireMode = (EFireMode)Memory.ReadValue<byte>(fireModePtr + Offsets.FireModeComponent.FireMode);
                    if (log)
                        Log.WriteLine($"[MagCheck] " + $"  FireMode   : 0x{fireModePtr:X16} +0x{Offsets.FireModeComponent.FireMode:X3} ? raw={fireMode} ({fireMode.GetDescription() ?? "unknown"})");
                    if (fireMode >= EFireMode.Auto && fireMode <= EFireMode.SemiAuto)
                        fireType = fireMode.GetDescription();
                }

                // Try to resolve ammo name from the chambered round.
                // Counts are accumulated below regardless of this path.
                try
                {
                    var chambers = Memory.ReadPtr(hands.ItemAddr + Offsets.LootItemWeapon.Chambers);
                    var slotPtr = Memory.ReadPtr(chambers + MemList<byte>.ArrStartOffset + 0 * 0x8);
                    var slotItem = Memory.ReadPtr(slotPtr + Offsets.Slot.ContainedItem);
                    var ammoTemplate = Memory.ReadPtr(slotItem + Offsets.LootItem.Template);
                    var idPtr = Memory.ReadValue<Types.MongoID>(ammoTemplate + Offsets.ItemTemplate._id);
                    string id = Memory.ReadUnityString(idPtr.StringID);
                    if (log)
                        Log.WriteLine($"[MagCheck] " + $"  Chamber ammo name path: chambers=0x{chambers:X16} slotPtr=0x{slotPtr:X16} slotItem=0x{slotItem:X16} ammoTemplate=0x{ammoTemplate:X16} id={id}");
                    if (EftDataManager.AllItems.TryGetValue(id, out var ammo))
                        ammoInChamber = ammo?.ShortName;
                }
                catch
                {
                    // No round in chamber – try to get ammo name from the magazine stack instead.
                    try
                    {
                        var ammoTemplate_ = GetAmmoTemplateFromWeapon(hands.ItemAddr);
                        var ammoIdPtr = Memory.ReadValue<Types.MongoID>(ammoTemplate_ + Offsets.ItemTemplate._id);
                        string ammoId = Memory.ReadUnityString(ammoIdPtr.StringID);
                        if (log)
                            Log.WriteLine($"[MagCheck] " + $"  Mag-stack ammo fallback: ammoTemplate=0x{ammoTemplate_:X16} id={ammoId}");
                        if (EftDataManager.AllItems.TryGetValue(ammoId, out var ammo))
                            ammoFromMag = ammo?.ShortName;
                    }
                    catch { }
                }

                var chambersPtr = Memory.ReadValue<ulong>(hands.ItemAddr + Offsets.LootItemWeapon.Chambers);
                if (log)
                {
                    string chambersClass = chambersPtr != 0 ? ObjectClass.ReadName(chambersPtr, useCache: false) : "null";
                    Log.WriteLine($"[MagCheck] " + $"  ChambersPtr: 0x{hands.ItemAddr:X16} +0x{Offsets.LootItemWeapon.Chambers:X3} ? ptr=0x{chambersPtr:X16}  class={chambersClass}");
                }

                if (chambersPtr != 0x0) // Single chamber, or for some shotguns, multiple chambers
                {
                    using var chambers = MemArray<Chamber>.Get(chambersPtr);
                    int loaded = chambers.Count(x => x.HasBullet());
                    currentCount += loaded;
                    ammoInChamber = GetLoadedAmmoName(chambers.FirstOrDefault(x => x.HasBullet()), log);
                    chamberSlotCount = chambers.Count;
                    maxCount += chamberSlotCount;
                    if (log)
                        Log.WriteLine($"[MagCheck] " + $"  Chambers   : count={chambers.Count} loaded={loaded} ammo={ammoInChamber ?? "null"}");
                }

                if (magSlotPtr != 0x0)
                {
                    var magItem = Memory.ReadValue<ulong>(magSlotPtr + Offsets.Slot.ContainedItem);
                    if (log)
                    {
                        string magItemClass = magItem != 0 ? ObjectClass.ReadName(magItem, useCache: false) : "null";
                        Log.WriteLine($"[MagCheck] " + $"  MagItem    : 0x{magSlotPtr:X16} +0x{Offsets.Slot.ContainedItem:X3} ? ptr=0x{magItem:X16}  class={magItemClass}");
                    }

                    if (magItem != 0x0)
                    {
                        var magChambersPtr = Memory.ReadPtr(magItem + Offsets.LootItemMod.Slots);
                        using var magChambers = MemArray<Chamber>.Get(magChambersPtr);
                        if (log)
                        {
                            string magChambersClass = magChambersPtr != 0 ? ObjectClass.ReadName(magChambersPtr, useCache: false) : "null";
                            Log.WriteLine($"[MagCheck] " + $"  MagChambers: 0x{magItem:X16} +0x{Offsets.LootItemMod.Slots:X3} ? ptr=0x{magChambersPtr:X16}  count={magChambers.Count}  class={magChambersClass}");
                        }

                        if (magChambers.Count > 0) // Revolvers, etc.
                        {
                            int loaded = magChambers.Count(x => x.HasBullet());
                            maxCount += magChambers.Count;
                            currentCount += loaded;
                            ammoInChamber ??= GetLoadedAmmoName(magChambers.FirstOrDefault(x => x.HasBullet()), log);
                            if (log)
                                Log.WriteLine($"[MagCheck] " + $"  Revolver path: magChambers={magChambers.Count} loaded={loaded} ammo={ammoInChamber ?? "null"}");
                        }
                        else // Regular magazines
                        {
                            maxCount -= chamberSlotCount; // chamber slot is not part of magazine capacity
                            // Step 1: read and immediately validate the Cartridges StackSlot pointer
                            var cartridges = Memory.ReadPtr(magItem + 0xA8);
                            string cartridgesClass = cartridges != 0 ? ObjectClass.ReadName(cartridges, useCache: false) : "null";
                            if (log)
                                Log.WriteLine($"[MagCheck] " + $"  Cartridges : 0x{magItem:X16} +0x{Offsets.LootItemMagazine.Cartridges:X3} ? 0x{cartridges:X16}  class={cartridgesClass}");

                            if (!cartridges.IsValidVirtualAddress())
                            {
                                if (log)
                                    Log.WriteLine($"[MagCheck] " + "  Cartridges INVALID — skipping regular mag path");
                            }
                            else
                            {
                                try
                                {
                                    // Step 2: read MaxCount and the stack-list pointer
                                    int slotMax = Memory.ReadValue<int>(cartridges + Offsets.StackSlot.MaxCount);
                                    maxCount += slotMax;
                                    var magStackPtr = Memory.ReadPtr(cartridges + Offsets.StackSlot._items);
                                    if (log)
                                        Log.WriteLine($"[MagCheck] " + $"  Regular mag: MaxCount@+0x{Offsets.StackSlot.MaxCount:X3}={slotMax}  stackList@+0x{Offsets.StackSlot._items:X3}=0x{magStackPtr:X16}");

                                    if (!magStackPtr.IsValidVirtualAddress())
                                    {
                                        if (log)
                                            Log.WriteLine($"[MagCheck] " + "  MagStack INVALID — skipping");
                                    }
                                    else
                                    {
                                        using var magStack = MemList<ulong>.Get(magStackPtr);
                                        int stackIdx = 0;
                                        foreach (var stack in magStack) // Each ammo type will be a separate stack
                                        {
                                            if (stack != 0x0)
                                            {
                                                int stackCount = Memory.ReadValue<int>(stack + Offsets.MagazineClass.StackObjectsCount, false);
                                                currentCount += stackCount;
                                                if (log)
                                                {
                                                    string stackClass = ObjectClass.ReadName(stack, useCache: false);
                                                    Log.WriteLine($"[MagCheck] " + $"  Stack[{stackIdx}]: 0x{stack:X16} +0x{Offsets.MagazineClass.StackObjectsCount:X3} = {stackCount}  class={stackClass}");
                                                }
                                            }
                                            stackIdx++;
                                        }
                                    }
                                }
                                catch (Exception regEx)
                                {
                                    if (log)
                                        Log.WriteLine($"[MagCheck] " + $"  Regular mag EXCEPTION at cartridges=0x{cartridges:X16}: {regEx.GetType().Name}: {regEx.Message}");
                                }
                            }
                        }
                    }
                }

                _ammo = ammoInChamber ?? ammoFromMag;
                _fireType = fireType;
                Count = currentCount;
                MaxCount = maxCount;

                if (log)
                    Log.WriteLine($"[MagCheck] " + $"?? RESULT: fireType={fireType ?? "null"} ammo={_ammo ?? "null"} count={currentCount}/{maxCount}");

                if (_ammo != null) _lastValidAmmo = _ammo;
                if (_fireType != null) _lastValidFireType = _fireType;
                if (currentCount > 0) _lastValidCount = currentCount;
                if (maxCount > 0) _lastValidMaxCount = maxCount;
            }

            /// <summary>
            /// Gets the name of the ammo round currently loaded in this chamber, otherwise NULL.
            /// </summary>
            /// <param name="chamber">Chamber to check.</param>
            /// <param name="log">When true, emit detailed address/offset trace via LoggingEnhancements.</param>
            /// <returns>Short name of ammo in chamber, or null if no round loaded.</returns>
            private static string GetLoadedAmmoName(Chamber chamber, bool log = false)
            {
                if (chamber != 0x0)
                {
                    var bulletItem = Memory.ReadValue<ulong>(chamber + Offsets.Slot.ContainedItem);
                    if (log)
                    {
                        string chamberClass    = ObjectClass.ReadName((ulong)chamber, useCache: false);
                        string bulletItemClass = bulletItem != 0 ? ObjectClass.ReadName(bulletItem, useCache: false) : "null";
                        Log.WriteLine($"[AmmoName] " + $"  Chamber  : 0x{(ulong)chamber:X16}  class={chamberClass}");
                        Log.WriteLine($"[AmmoName] " + $"  BulletItem: 0x{(ulong)chamber:X16} +0x{Offsets.Slot.ContainedItem:X3} ? 0x{bulletItem:X16}  class={bulletItemClass}");
                    }
                    if (bulletItem != 0x0)
                    {
                        var bulletTemp = Memory.ReadPtr(bulletItem + Offsets.LootItem.Template);
                        var bulletIdPtr = Memory.ReadValue<Types.MongoID>(bulletTemp + Offsets.ItemTemplate._id);
                        var bulletId = Memory.ReadUnityString(bulletIdPtr.StringID, 32);
                        if (log)
                        {
                            string bulletTempClass = bulletTemp != 0 ? ObjectClass.ReadName(bulletTemp, useCache: false) : "null";
                            Log.WriteLine($"[AmmoName] " + $"  Template : 0x{bulletItem:X16} +0x{Offsets.LootItem.Template:X3} ? 0x{bulletTemp:X16}  class={bulletTempClass}  id@+0x{Offsets.ItemTemplate._id:X3} ? {bulletId}");
                        }
                        if (EftDataManager.AllItems.TryGetValue(bulletId, out var bullet))
                        {
                            if (log)
                                Log.WriteLine($"[AmmoName] " + $"  Resolved : {bullet?.ShortName ?? "null"}");
                            return bullet?.ShortName;
                        }
                        if (log)
                            Log.WriteLine($"[AmmoName] " + "  Resolved : NOT FOUND in AllItems");
                    }
                }
                else if (log)
                {
                    Log.WriteLine($"[AmmoName] " + "  Chamber  : null (0x0)");
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
                bool log = DebugLogging &&
                            Log.TryThrottle("AmmoTemplate", TimeSpan.FromSeconds(1));

                var chambersPtr = Memory.ReadValue<ulong>(lootItemBase + Offsets.LootItemWeapon.Chambers);
                if (log)
                {
                    string weaponClass   = ObjectClass.ReadName(lootItemBase, useCache: false);
                    string chambersClass = chambersPtr != 0 ? ObjectClass.ReadName(chambersPtr, useCache: false) : "null";
                    Log.WriteLine($"[AmmoTemplate] " + $"-- WeaponBase: 0x{lootItemBase:X16}  class={weaponClass}  ChambersPtr@+0x{Offsets.LootItemWeapon.Chambers:X3}=0x{chambersPtr:X16}  class={chambersClass}");
                }

                ulong firstRound;
                MemArray<Chamber> chambers = null;
                MemArray<Chamber> magChambers = null;
                MemList<ulong> magStack = null;
                try
                {
                    if (chambersPtr != 0x0 && (chambers = MemArray<Chamber>.Get(chambersPtr)).Count > 0) // Single chamber, or for some shotguns, multiple chambers
                    {
                        var loaded = chambers.FirstOrDefault(x => x.HasBullet(true));
                        if (log)
                            Log.WriteLine($"[AmmoTemplate] " + $"  Chamber path: count={chambers.Count} loaded={(ulong)loaded:X16}");
                        if (loaded == default)
                            throw new InvalidOperationException("No loaded round found in chambers");
                        firstRound = Memory.ReadPtr(loaded + Offsets.Slot.ContainedItem);
                        if (log)
                            Log.WriteLine($"[AmmoTemplate] " + $"  firstRound : 0x{(ulong)loaded:X16} +0x{Offsets.Slot.ContainedItem:X3} ? 0x{firstRound:X16}");
                    }
                    else
                    {
                        var magSlot = Memory.ReadPtr(lootItemBase + Offsets.LootItemWeapon._magSlotCache);
                        var magItemPtr = Memory.ReadPtr(magSlot + Offsets.Slot.ContainedItem);
                        var magChambersPtr = Memory.ReadPtr(magItemPtr + Offsets.LootItemMod.Slots);
                        magChambers = MemArray<Chamber>.Get(magChambersPtr);
                        if (log)
                        {
                            string magSlotClass    = magSlot    != 0 ? ObjectClass.ReadName(magSlot,    useCache: false) : "null";
                            string magItemClass    = magItemPtr != 0 ? ObjectClass.ReadName(magItemPtr, useCache: false) : "null";
                            string magChambersClass = magChambersPtr != 0 ? ObjectClass.ReadName(magChambersPtr, useCache: false) : "null";
                            Log.WriteLine($"[AmmoTemplate] " + $"  Mag path: magSlot=0x{magSlot:X16}  class={magSlotClass}  magItem=0x{magItemPtr:X16}  class={magItemClass}  magChambers@+0x{Offsets.LootItemMod.Slots:X3}=0x{magChambersPtr:X16}  class={magChambersClass}  count={magChambers.Count}");
                        }
                        if (magChambers.Count > 0) // Revolvers, etc.
                        {
                            var loaded = magChambers.FirstOrDefault(x => x.HasBullet(true));
                            if (log)
                                Log.WriteLine($"[AmmoTemplate] " + $"  Revolver path: loaded=0x{(ulong)loaded:X16}");
                            if (loaded == default)
                                throw new InvalidOperationException("No loaded round found in magazine chambers");
                            firstRound = Memory.ReadPtr(loaded + Offsets.Slot.ContainedItem);
                            if (log)
                                Log.WriteLine($"[AmmoTemplate] " + $"  firstRound : 0x{(ulong)loaded:X16} +0x{Offsets.Slot.ContainedItem:X3} ? 0x{firstRound:X16}");
                        }
                        else // Regular magazines
                        {
                            var cartridges = Memory.ReadPtr(magItemPtr + 0xA8);
                            var magStackPtr = Memory.ReadPtr(cartridges + Offsets.StackSlot._items);
                            magStack = MemList<ulong>.Get(magStackPtr);
                            firstRound = magStack[0];
                            if (log)
                                Log.WriteLine($"[AmmoTemplate] " + $"  Regular mag: cartridges=0x{cartridges:X16} stackList@+0x{Offsets.StackSlot._items:X3}=0x{magStackPtr:X16} stack[0]=0x{firstRound:X16}");
                        }
                    }
                    var result = Memory.ReadPtr(firstRound + Offsets.LootItem.Template);
                    if (log)
                    {
                        string roundClass  = firstRound != 0 ? ObjectClass.ReadName(firstRound, useCache: false) : "null";
                        string resultClass = result     != 0 ? ObjectClass.ReadName(result,     useCache: false) : "null";
                        Log.WriteLine($"[AmmoTemplate] " + $"  firstRound: 0x{firstRound:X16}  class={roundClass}");
                        Log.WriteLine($"[AmmoTemplate] " + $"-- Template : 0x{firstRound:X16} +0x{Offsets.LootItem.Template:X3} ? 0x{result:X16}  class={resultClass}");
                    }
                    return result;
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