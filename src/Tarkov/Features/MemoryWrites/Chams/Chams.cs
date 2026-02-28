using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.GameWorld;
using static eft_dma_radar.Tarkov.Features.MemoryWrites.Chams.ChamsConfig;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites.Chams
{
    public sealed class Chams : MemWriteFeature<Chams>
    {
        private bool _lastEnabledState;
        private readonly Dictionary<ChamsEntityType, EntityChamsSettings> _lastEntityStates = new();

        public static ChamsConfig Config => MemWrites.Config.Chams;

        public override bool Enabled
        {
            get => Config.Enabled;
            set => Config.Enabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(100);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (!Memory.InRaid || Memory.Game is not LocalGameWorld game)
                    return;

                var stateChanged = HasStateChanged();

                if (!Enabled && _lastEnabledState)
                {
                    DisableAllChams(writes, game);
                    return;
                }

                if (!Enabled)
                    return;

                if (RequiresOcclusionDisable())
                    DisableOcclusionCulling(writes, game.CameraManager);

                PlayerChamsManager.ProcessPlayerChams(writes, game);
                LootChamsManager.ProcessLootChams();

                UpdateStateTracking();
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Chams]: {ex}");
            }
        }

        private bool HasStateChanged()
        {
            if (Enabled != _lastEnabledState)
                return true;

            return CheckEntityStatesChanged();
        }

        private bool CheckEntityStatesChanged()
        {
            foreach (var kvp in Config.EntityChams)
            {
                var entityType = kvp.Key;
                var currentSettings = kvp.Value;

                if (!_lastEntityStates.TryGetValue(entityType, out var lastSettings))
                    return true;

                if (HasEntitySettingsChanged(currentSettings, lastSettings))
                    return true;
            }

            return false;
        }

        private bool HasEntitySettingsChanged(EntityChamsSettings current, EntityChamsSettings last)
        {
            return current.Enabled != last.Enabled ||
                   current.Mode != last.Mode ||
                   current.ClothingChamsMode != last.ClothingChamsMode ||
                   current.GearChamsMode != last.GearChamsMode ||
                   current.VisibleColor != last.VisibleColor ||
                   current.InvisibleColor != last.InvisibleColor ||
                   current.RevertOnDeath != last.RevertOnDeath;
        }

        private void UpdateStateTracking()
        {
            _lastEnabledState = Enabled;
            _lastEntityStates.Clear();

            foreach (var kvp in Config.EntityChams)
            {
                _lastEntityStates[kvp.Key] = new EntityChamsSettings
                {
                    Enabled = kvp.Value.Enabled,
                    Mode = kvp.Value.Mode,
                    ClothingChamsMode = kvp.Value.ClothingChamsMode,
                    GearChamsMode = kvp.Value.GearChamsMode,
                    VisibleColor = kvp.Value.VisibleColor,
                    InvisibleColor = kvp.Value.InvisibleColor,
                    RevertOnDeath = kvp.Value.RevertOnDeath
                };
            }
        }

        private void DisableAllChams(ScatterWriteHandle writes, LocalGameWorld game)
        {
            RestoreOcclusionCulling(writes, game.CameraManager);
            PlayerChamsManager.RevertAllPlayerChams(writes, game);
            LootChamsManager.RevertAllLootChams();

            writes.Callbacks += () => _lastEnabledState = false;
        }

        private bool RequiresOcclusionDisable()
        {
            return Config.EntityChams.Values.Any(settings =>
                settings.Enabled && IsAdvancedMode(settings.Mode, settings.ClothingChamsMode, settings.GearChamsMode));
        }

        private bool IsAdvancedMode(ChamsMode mode, ChamsMode clothingMode, ChamsMode gearMode)
        {
            return IsAdvanced(mode) || IsAdvanced(clothingMode) || IsAdvanced(gearMode);
        }

        private bool IsAdvanced(ChamsMode mode)
        {
            return mode is ChamsMode.VisCheckGlow or ChamsMode.VisCheckFlat or ChamsMode.WireFrame;
        }

        private static void DisableOcclusionCulling(ScatterWriteHandle writes, CameraManager cameraManager)
        {
            const bool targetCulling = false;

            var fpsView = cameraManager.FPSCamera;
            var opticView = cameraManager.OpticCamera;

            var cullingFps = Memory.ReadValue<bool>(fpsView + UnityOffsets.Camera.OcclusionCulling);
            var cullingOptical = Memory.ReadValue<bool>(opticView + UnityOffsets.Camera.OcclusionCulling);

            if (cullingFps != targetCulling)
                writes.AddValueEntry(fpsView + UnityOffsets.Camera.OcclusionCulling, targetCulling);

            if (cullingOptical != targetCulling)
                writes.AddValueEntry(opticView + UnityOffsets.Camera.OcclusionCulling, targetCulling);
        }

        private static void RestoreOcclusionCulling(ScatterWriteHandle writes, CameraManager cameraManager)
        {
            const bool originalCulling = true;

            var fpsView = cameraManager.FPSCamera;
            var opticView = cameraManager.OpticCamera;

            writes.AddValueEntry(fpsView + UnityOffsets.Camera.OcclusionCulling, originalCulling);
            writes.AddValueEntry(opticView + UnityOffsets.Camera.OcclusionCulling, originalCulling);
        }

        public override void OnRaidStart()
        {
            ResetState();
            InitializeManagers();
        }

        public override void OnGameStop()
        {
            ResetState();
            PlayerChamsManager.Reset();
            LootChamsManager.Reset();
        }

        private void ResetState()
        {
            _lastEnabledState = default;
            _lastEntityStates.Clear();
        }

        private void InitializeManagers()
        {
            Config.InitializeDefaults();

            PlayerChamsManager.Initialize();
            LootChamsManager.Initialize();
        }
    }
}