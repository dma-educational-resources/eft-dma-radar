using eft_dma_radar;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.Unity.IL2CPP;
using eft_dma_radar.UI.Misc;
using eft_dma_shared.Common.Unity;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class LootThroughWalls : MemWriteFeature<LootThroughWalls>
    {
        private bool _initialized;
        private bool _zoomEngaged;
        private ulong _lastFirearmController;
        private float _lastFovCompensatoryDist;
        private ulong _cachedGameWorld;
        private ulong _cachedHardSettings;

        private const float WEAPON_LN_ZOOM = 0.001f;
        private const float WEAPON_LN_ORIGINAL = -1f;
        private const float FOV_COMPENSATORY_DIST_ORIGINAL = 0f;

        /// <summary>
        /// True if LTW Zoom is engaged.
        /// </summary>
        public static bool ZoomEngaged { get; set; }

        public override bool Enabled
        {
            get => MemWrites.Config.LootThroughWalls.Enabled;
            set => MemWrites.Config.LootThroughWalls.Enabled = value;
        }

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                if (!_initialized && Enabled)
                    InitializeLTW();

                var hc = localPlayer.Firearm?.HandsController;

                if (hc?.Item2 is bool firearm && firearm && hc.Item1 is ulong firearmController)
                    HandleZoomLogic(writes, localPlayer, firearmController);
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[LootThroughWalls]: {ex}");
                ClearCache();
            }
        }

        private void InitializeLTW()
        {
            try
            {
                XMLogging.WriteLine("[LootThroughWalls] Initializing...");
                if (Memory.Game is not LocalGameWorld game)
                    return;
                var gameWorld = game.Base;
                if (!gameWorld.IsValidVirtualAddress())
                    throw new InvalidOperationException("Failed to get GameWorld instance");

                var hardSettings = GetHardSettings();
                if (!hardSettings.IsValidVirtualAddress())
                    throw new InvalidOperationException("Failed to get EFTHardSettings instance");

                Memory.WriteValueEnsure<int>(gameWorld + 0x18, 0);

                _initialized = true;
                XMLogging.WriteLine("[LootThroughWalls] Initialized successfully!");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[LootThroughWalls] Initialization failed: {ex}");
            }
        }

        private void HandleZoomLogic(ScatterWriteHandle writes, LocalPlayer localPlayer, ulong firearmController)
        {
            var shouldEngage = ZoomEngaged && Enabled;
            var configZoomAmount = MemWrites.Config.LootThroughWalls.ZoomAmount;
            var controllerChanged = firearmController != _lastFirearmController;
            var zoomAmountChanged = configZoomAmount != _lastFovCompensatoryDist;
            var stateChanged = shouldEngage != _zoomEngaged;

            if (shouldEngage && (stateChanged || controllerChanged || zoomAmountChanged))
            {
                writes.AddValueEntry(firearmController + Offsets.ClientFirearmController.WeaponLn, WEAPON_LN_ZOOM);
                writes.AddValueEntry(localPlayer.PWA + Offsets.ProceduralWeaponAnimation._fovCompensatoryDistance, configZoomAmount);

                writes.Callbacks += () =>
                {
                    _zoomEngaged = true;
                    _lastFovCompensatoryDist = configZoomAmount;
                    _lastFirearmController = firearmController;
                    XMLogging.WriteLine($"[LootThroughWalls] Zoom Enabled (Amount: {configZoomAmount:F2})");
                };
            }
            else if (!shouldEngage && _zoomEngaged)
            {
                writes.AddValueEntry(firearmController + Offsets.ClientFirearmController.WeaponLn, WEAPON_LN_ORIGINAL);
                writes.AddValueEntry(localPlayer.PWA + Offsets.ProceduralWeaponAnimation._fovCompensatoryDistance, FOV_COMPENSATORY_DIST_ORIGINAL);

                writes.Callbacks += () =>
                {
                    _zoomEngaged = false;
                    _lastFovCompensatoryDist = FOV_COMPENSATORY_DIST_ORIGINAL;
                    XMLogging.WriteLine("[LootThroughWalls] Zoom Disabled");
                };
            }
        }

        private ulong GetGameWorld()
        {
            if (_cachedGameWorld.IsValidVirtualAddress())
                return _cachedGameWorld;

            try
            {
                var gameWorld = Il2CppClass.Find("Assembly-CSharp", "EFT.GameWorld", out _);
                if (!gameWorld.IsValidVirtualAddress())
                    return 0x0;

                _cachedGameWorld = gameWorld;
                return gameWorld;
            }
            catch
            {
                return 0x0;
            }
        }

        private ulong GetHardSettings()
        {
            if (_cachedHardSettings.IsValidVirtualAddress())
                return _cachedHardSettings;

            try
            {
                var hardSettingsInstance = EftHardSettingsResolver.GetInstance();
                if (!Utils.IsValidVirtualAddress(hardSettingsInstance))
                {
                    Debug.WriteLine("[FastDuck] EFTHardSettings.Instance not found.");
                    EftHardSettingsResolver.InvalidateCache();
                    //_lastEnabledState = Enabled;
                    return 0x0;
                }

                _cachedHardSettings = hardSettingsInstance;
                return hardSettingsInstance;
            }
            catch
            {
                return 0x0;
            }
        }

        private void ClearCache()
        {
            _cachedGameWorld = default;
            _cachedHardSettings = default;
        }

        public override void OnRaidStart()
        {
            _initialized = default;
            _zoomEngaged = default;
            _lastFirearmController = default;
            _lastFovCompensatoryDist = default;
            ClearCache();
        }
    }
}