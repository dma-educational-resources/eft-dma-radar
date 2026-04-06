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
                            if (!Memory.TryReadPtrChain(hands, Offsets.FirearmController.To_FirePortVertices, out var v, false)
                                || fireportTransform.VerticesAddr != v)
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
                            if (!Memory.TryReadPtrChain(hands,
                                Offsets.FirearmController.To_FirePortTransformInternal, out var t, false)
                                || !t.IsValidVirtualAddress())
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
            if (!Memory.TryReadPtr(handsController + Offsets.ItemHandsController.Item, out var itemBase, false))
                return new(handsController);
            if (!Memory.TryReadPtr(itemBase + Offsets.LootItem.Template, out var itemTemp, false))
                return new(handsController);
            if (!Memory.TryReadValue<Types.MongoID>(itemTemp + Offsets.ItemTemplate._id, out var itemIdPtr, false))
                return new(handsController);
            if (!Memory.TryReadUnityString(itemIdPtr.StringID, out var itemId, 64, false) || itemId is null)
                return new(handsController);
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

                var fireModePtr = Memory.TryReadValue<ulong>(hands.ItemAddr + Offsets.LootItemWeapon.FireMode, out var fmp) ? fmp : 0UL;
                var magSlotPtr = Memory.TryReadValue<ulong>(hands.ItemAddr + Offsets.LootItemWeapon._magSlotCache, out var msp) ? msp : 0UL;

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
                    if (Memory.TryReadValue<byte>(fireModePtr + Offsets.FireModeComponent.FireMode, out var fireModeRaw))
                    {
                        var fireMode = (EFireMode)fireModeRaw;
                        if (log)
                            Log.WriteLine($"[MagCheck] " + $"  FireMode   : 0x{fireModePtr:X16} +0x{Offsets.FireModeComponent.FireMode:X3} ? raw={fireMode} ({fireMode.GetDescription() ?? "unknown"})");
                        if (fireMode >= EFireMode.Auto && fireMode <= EFireMode.SemiAuto)
                            fireType = fireMode.GetDescription();
                    }
                }

                // Try to resolve ammo name from the chambered round.
                // Counts are accumulated below regardless of this path.
                if (Memory.TryReadPtr(hands.ItemAddr + Offsets.LootItemWeapon.Chambers, out var chambersTmp)
                    && Memory.TryReadPtr(chambersTmp + MemList<byte>.ArrStartOffset + 0 * 0x8, out var slotPtr2)
                    && Memory.TryReadPtr(slotPtr2 + Offsets.Slot.ContainedItem, out var slotItem)
                    && Memory.TryReadPtr(slotItem + Offsets.LootItem.Template, out var ammoTemplate)
                    && Memory.TryReadValue<Types.MongoID>(ammoTemplate + Offsets.ItemTemplate._id, out var idPtr2)
                    && Memory.TryReadUnityString(idPtr2.StringID, out var chamberId) && chamberId is not null)
                {
                    if (log)
                        Log.WriteLine($"[MagCheck] " + $"  Chamber ammo name path: chambers=0x{chambersTmp:X16} slotPtr=0x{slotPtr2:X16} slotItem=0x{slotItem:X16} ammoTemplate=0x{ammoTemplate:X16} id={chamberId}");
                    if (EftDataManager.AllItems.TryGetValue(chamberId, out var ammo))
                        ammoInChamber = ammo?.ShortName;
                }
                else
                {
                    // No round in chamber — try to get ammo name from the magazine stack instead.
                    try
                    {
                        var ammoTemplate_ = GetAmmoTemplateFromWeapon(hands.ItemAddr);
                        if (ammoTemplate_ != 0
                            && Memory.TryReadValue<Types.MongoID>(ammoTemplate_ + Offsets.ItemTemplate._id, out var ammoIdPtr)
                            && Memory.TryReadUnityString(ammoIdPtr.StringID, out var ammoId) && ammoId is not null)
                        {
                            if (log)
                                Log.WriteLine($"[MagCheck] " + $"  Mag-stack ammo fallback: ammoTemplate=0x{ammoTemplate_:X16} id={ammoId}");
                            if (EftDataManager.AllItems.TryGetValue(ammoId, out var ammo))
                                ammoFromMag = ammo?.ShortName;
                        }
                    }
                    catch { }
                }

                Memory.TryReadValue<ulong>(hands.ItemAddr + Offsets.LootItemWeapon.Chambers, out var chambersPtr);
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
                    Memory.TryReadValue<ulong>(magSlotPtr + Offsets.Slot.ContainedItem, out var magItem);
                    if (log)
                    {
                        string magItemClass = magItem != 0 ? (ObjectClass.TryReadName(magItem, out var mn, useCache: false) ? mn : "?") : "null";
                        Log.WriteLine($"[MagCheck] " + $"  MagItem    : 0x{magSlotPtr:X16} +0x{Offsets.Slot.ContainedItem:X3} ? ptr=0x{magItem:X16}  class={magItemClass}");
                    }

                    if (magItem != 0x0 && Memory.TryReadPtr(magItem + Offsets.LootItemMod.Slots, out var magChambersPtr))
                    {
                        using var magChambers = MemArray<Chamber>.Get(magChambersPtr);
                        if (log)
                        {
                            string magChambersClass = magChambersPtr != 0 ? (ObjectClass.TryReadName(magChambersPtr, out var mcn, useCache: false) ? mcn : "?") : "null";
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
                            if (Memory.TryReadPtr(magItem + 0xA8, out var cartridges))
                            {
                                if (log)
                                {
                                    string cartridgesClass = cartridges != 0 ? (ObjectClass.TryReadName(cartridges, out var ccn, useCache: false) ? ccn : "?") : "null";
                                    Log.WriteLine($"[MagCheck] " + $"  Cartridges : 0x{magItem:X16} +0x{Offsets.LootItemMagazine.Cartridges:X3} ? 0x{cartridges:X16}  class={cartridgesClass}");
                                }

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
                                        if (Memory.TryReadValue<int>(cartridges + Offsets.StackSlot.MaxCount, out var slotMax))
                                            maxCount += slotMax;
                                        if (Memory.TryReadPtr(cartridges + Offsets.StackSlot._items, out var magStackPtr2))
                                        {
                                            if (log)
                                                Log.WriteLine($"[MagCheck] " + $"  Regular mag: MaxCount@+0x{Offsets.StackSlot.MaxCount:X3}={slotMax}  stackList@+0x{Offsets.StackSlot._items:X3}=0x{magStackPtr2:X16}");

                                            if (!magStackPtr2.IsValidVirtualAddress())
                                            {
                                                if (log)
                                                    Log.WriteLine($"[MagCheck] " + "  MagStack INVALID — skipping");
                                            }
                                            else
                                            {
                                                using var magStack = MemList<ulong>.Get(magStackPtr2);
                                                int stackIdx = 0;
                                                foreach (var stack in magStack) // Each ammo type will be a separate stack
                                                {
                                                    if (stack != 0x0)
                                                    {
                                                        if (Memory.TryReadValue<int>(stack + Offsets.MagazineClass.StackObjectsCount, out var stackCount, false))
                                                            currentCount += stackCount;
                                                        if (log)
                                                        {
                                                            string stackClass = ObjectClass.TryReadName(stack, out var scn, useCache: false) ? scn : "?";
                                                            Log.WriteLine($"[MagCheck] " + $"  Stack[{stackIdx}]: 0x{stack:X16} +0x{Offsets.MagazineClass.StackObjectsCount:X3} = {stackCount}  class={stackClass}");
                                                        }
                                                    }
                                                    stackIdx++;
                                                }
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
                    Memory.TryReadValue<ulong>(chamber + Offsets.Slot.ContainedItem, out var bulletItem);
                    if (log)
                    {
                        string chamberClass    = ObjectClass.TryReadName((ulong)chamber, out var ccn, useCache: false) ? ccn : "?";
                        string bulletItemClass = bulletItem != 0 ? (ObjectClass.TryReadName(bulletItem, out var bcn, useCache: false) ? bcn : "?") : "null";
                        Log.WriteLine($"[AmmoName] " + $"  Chamber  : 0x{(ulong)chamber:X16}  class={chamberClass}");
                        Log.WriteLine($"[AmmoName] " + $"  BulletItem: 0x{(ulong)chamber:X16} +0x{Offsets.Slot.ContainedItem:X3} ? 0x{bulletItem:X16}  class={bulletItemClass}");
                    }
                    if (bulletItem != 0x0
                        && Memory.TryReadPtr(bulletItem + Offsets.LootItem.Template, out var bulletTemp)
                        && Memory.TryReadValue<Types.MongoID>(bulletTemp + Offsets.ItemTemplate._id, out var bulletIdPtr)
                        && Memory.TryReadUnityString(bulletIdPtr.StringID, out var bulletId, 32) && bulletId is not null)
                    {
                        if (log)
                        {
                            string bulletTempClass = bulletTemp != 0 ? (ObjectClass.TryReadName(bulletTemp, out var btcn, useCache: false) ? btcn : "?") : "null";
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

                Memory.TryReadValue<ulong>(lootItemBase + Offsets.LootItemWeapon.Chambers, out var chambersPtr);
                if (log)
                {
                    string weaponClass   = ObjectClass.TryReadName(lootItemBase, out var wcn, useCache: false) ? wcn : "?";
                    string chambersClass = chambersPtr != 0 ? (ObjectClass.TryReadName(chambersPtr, out var ccn, useCache: false) ? ccn : "?") : "null";
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
                        if (!Memory.TryReadPtr(loaded + Offsets.Slot.ContainedItem, out firstRound))
                            throw new InvalidOperationException("Failed to read chamber contained item");
                        if (log)
                            Log.WriteLine($"[AmmoTemplate] " + $"  firstRound : 0x{(ulong)loaded:X16} +0x{Offsets.Slot.ContainedItem:X3} ? 0x{firstRound:X16}");
                    }
                    else
                    {
                        if (!Memory.TryReadPtr(lootItemBase + Offsets.LootItemWeapon._magSlotCache, out var magSlot)
                            || !Memory.TryReadPtr(magSlot + Offsets.Slot.ContainedItem, out var magItemPtr)
                            || !Memory.TryReadPtr(magItemPtr + Offsets.LootItemMod.Slots, out var magChambersPtr2))
                            throw new InvalidOperationException("Failed to read magazine chain");
                        magChambers = MemArray<Chamber>.Get(magChambersPtr2);
                        if (log)
                        {
                            string magSlotClass    = magSlot       != 0 ? (ObjectClass.TryReadName(magSlot,       out var mscn, useCache: false) ? mscn : "?") : "null";
                            string magItemClass    = magItemPtr    != 0 ? (ObjectClass.TryReadName(magItemPtr,    out var micn, useCache: false) ? micn : "?") : "null";
                            string magChambersClass = magChambersPtr2 != 0 ? (ObjectClass.TryReadName(magChambersPtr2, out var mccn, useCache: false) ? mccn : "?") : "null";
                            Log.WriteLine($"[AmmoTemplate] " + $"  Mag path: magSlot=0x{magSlot:X16}  class={magSlotClass}  magItem=0x{magItemPtr:X16}  class={magItemClass}  magChambers@+0x{Offsets.LootItemMod.Slots:X3}=0x{magChambersPtr2:X16}  class={magChambersClass}  count={magChambers.Count}");
                        }
                        if (magChambers.Count > 0) // Revolvers, etc.
                        {
                            var loaded = magChambers.FirstOrDefault(x => x.HasBullet(true));
                            if (log)
                                Log.WriteLine($"[AmmoTemplate] " + $"  Revolver path: loaded=0x{(ulong)loaded:X16}");
                            if (loaded == default)
                                throw new InvalidOperationException("No loaded round found in magazine chambers");
                            if (!Memory.TryReadPtr(loaded + Offsets.Slot.ContainedItem, out firstRound))
                                throw new InvalidOperationException("Failed to read revolver contained item");
                            if (log)
                                Log.WriteLine($"[AmmoTemplate] " + $"  firstRound : 0x{(ulong)loaded:X16} +0x{Offsets.Slot.ContainedItem:X3} ? 0x{firstRound:X16}");
                        }
                        else // Regular magazines
                        {
                            if (!Memory.TryReadPtr(magItemPtr + 0xA8, out var cartridges)
                                || !Memory.TryReadPtr(cartridges + Offsets.StackSlot._items, out var magStackPtr2))
                                throw new InvalidOperationException("Failed to read cartridges chain");
                            magStack = MemList<ulong>.Get(magStackPtr2);
                            firstRound = magStack[0];
                            if (log)
                                Log.WriteLine($"[AmmoTemplate] " + $"  Regular mag: cartridges=0x{cartridges:X16} stackList@+0x{Offsets.StackSlot._items:X3}=0x{magStackPtr2:X16} stack[0]=0x{firstRound:X16}");
                        }
                    }
                    if (!Memory.TryReadPtr(firstRound + Offsets.LootItem.Template, out var result))
                        throw new InvalidOperationException("Failed to read ammo template");
                    if (log)
                    {
                        string roundClass  = firstRound != 0 ? (ObjectClass.TryReadName(firstRound, out var rcn, useCache: false) ? rcn : "?") : "null";
                        string resultClass = result     != 0 ? (ObjectClass.TryReadName(result,     out var rscn, useCache: false) ? rscn : "?") : "null";
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
                    return Memory.TryReadValue<ulong>(_base + Offsets.Slot.ContainedItem, out var val, useCache) && val != 0x0;
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