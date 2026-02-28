using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.GameWorld;
using System.Collections.Concurrent;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class BigHead : MemWriteFeature<BigHead>
    {
        private readonly ConcurrentDictionary<ulong, Vector3> _modifiedPlayers = new();
        private bool _lastEnabledState;
        private float _lastScale;

        private static readonly Vector3 DEFAULT_SCALE = new(1f, 1f, 1f);

        public override bool Enabled
        {
            get => MemWrites.Config.BigHead.Enabled;
            set => MemWrites.Config.BigHead.Enabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(100);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game)
                    return;

                var currentScale = MemWrites.Config.BigHead.Scale;
                var stateChanged = Enabled != _lastEnabledState;
                var scaleChanged = Math.Abs(currentScale - _lastScale) > 0.001f;

                if (Enabled)
                    ApplyBigHeadToPlayers(writes, game, currentScale, stateChanged, scaleChanged);
                else if (stateChanged && _modifiedPlayers.Count > 0)
                    ResetAllHeadScales(writes, game);

                if (stateChanged || scaleChanged)
                {
                    _lastEnabledState = Enabled;
                    _lastScale = currentScale;
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[BigHead]: {ex}");
            }
        }

        private void ApplyBigHeadToPlayers(ScatterWriteHandle writes, LocalGameWorld game, float scale, bool stateChanged, bool scaleChanged)
        {
            var newScale = new Vector3(scale, scale, scale);
            var hostilePlayers = game.Players.Where(x => x.IsHostileActive).ToList();

            if (!hostilePlayers.Any())
            {
                if (_modifiedPlayers.Count > 0)
                    _modifiedPlayers.Clear();
                return;
            }

            var hasWrites = false;
            var playersToReset = new List<Player>();

            foreach (var playerEntry in _modifiedPlayers.ToList())
            {
                var playerBase = playerEntry.Key;
                var player = game.Players.FirstOrDefault(p => p.Base == playerBase);

                if (player == null || !player.IsAlive)
                {
                    if (player != null && player.Skeleton.Bones.TryGetValue(Bones.HumanHead, out var headTransform))
                    {
                        ApplyHeadScale(writes, player, headTransform, DEFAULT_SCALE);
                        playersToReset.Add(player);
                        hasWrites = true;
                    }
                    _modifiedPlayers.TryRemove(playerBase, out _);
                }
            }

            if (playersToReset.Count > 0)
            {
                writes.Callbacks += () =>
                {
                    var names = string.Join(", ", playersToReset.Select(p => p.Name));
                    var deadPlayers = playersToReset.Where(p => !p.IsAlive).ToList();
                    var action = deadPlayers.Count > 0 ? "died" : "disconnected";
                    XMLogging.WriteLine($"[BigHead] Reset scales for {playersToReset.Count} player(s) who {action}: {names}");
                };
            }

            foreach (var player in hostilePlayers)
            {
                if (player.ErrorTimer.IsRunning && player.ErrorTimer.Elapsed.TotalMilliseconds > 100)
                    continue;

                var needsUpdate = stateChanged || scaleChanged ||
                                 !_modifiedPlayers.TryGetValue(player.Base, out var currentScale) ||
                                 currentScale != newScale;

                if (needsUpdate && player.Skeleton.Bones.TryGetValue(Bones.HumanHead, out var headTransform))
                {
                    ApplyHeadScale(writes, player, headTransform, newScale);
                    _modifiedPlayers[player.Base] = newScale;
                    hasWrites = true;
                }
            }

            if (hasWrites && stateChanged && Enabled)
                writes.Callbacks += () => XMLogging.WriteLine($"[BigHead] Enabled (Scale: {scale:F1})");
        }

        private static void ApplyHeadScale(ScatterWriteHandle writes, Player player, UnityTransform headTransform, Vector3 scale)
        {
            try
            {
                var offset = headTransform.VerticesAddr + (uint)(headTransform.Index * 0x30) + 0x20;
                writes.AddValueEntry(offset, scale);
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[BigHead] Error applying head scale to player '{player.Name}': {ex}");
            }
        }

        private void ResetAllHeadScales(ScatterWriteHandle writes, LocalGameWorld game)
        {
            try
            {
                var resetCount = 0;

                foreach (var playerEntry in _modifiedPlayers)
                {
                    var playerBase = playerEntry.Key;
                    var player = game.Players.FirstOrDefault(p => p.Base == playerBase);

                    if (player?.IsActive == true &&
                        player.Skeleton.Bones.TryGetValue(Bones.HumanHead, out var headTransform))
                    {
                        var offset = headTransform.VerticesAddr + (uint)(headTransform.Index * 0x30) + 0x20;
                        writes.AddValueEntry(offset, DEFAULT_SCALE);
                        resetCount++;
                    }
                }

                _modifiedPlayers.Clear();

                if (resetCount > 0)
                {
                    writes.Callbacks += () => XMLogging.WriteLine($"[BigHead] Disabled (Reset {resetCount} player head scales)");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[BigHead] Error resetting head scales: {ex}");
            }
        }

        public override void OnRaidStart()
        {
            _modifiedPlayers.Clear();
            _lastEnabledState = default;
            _lastScale = default;
        }
    }
}
