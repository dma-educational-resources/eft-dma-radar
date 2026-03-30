using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Unity.Collections;
using eft_dma_radar.Tarkov.Features.MemoryWrites.Patches;

namespace eft_dma_radar.Tarkov.GameWorld
{
    public sealed class RegisteredPlayers : IReadOnlyCollection<Player>
    {
        #region Fields/Properties/Constructor

        public static implicit operator ulong(RegisteredPlayers x) => x.Base;
        private ulong Base { get; }
        private readonly LocalGameWorld _game;
        private readonly ConcurrentDictionary<ulong, Player> _players = new();
        /// <summary>
        /// Tracks failed player allocations to prevent spam. Key=playerBase, Value=(failCount, lastAttemptTime)
        /// </summary>
        private readonly ConcurrentDictionary<ulong, (int Count, DateTime LastAttempt)> _failedAllocations = new();
        private readonly HashSet<ulong> _registeredScratch = new(64);
        private const int MAX_FAIL_COUNT = 5;
        private static readonly TimeSpan FAIL_RETRY_COOLDOWN = TimeSpan.FromSeconds(2);
        private int _refreshTick;

        /// <summary>
        /// LocalPlayer Instance.
        /// </summary>
        public LocalPlayer LocalPlayer { get; }

        /// <summary>
        /// RegisteredPlayers List Constructor.
        /// </summary>
        public RegisteredPlayers(ulong baseAddr, LocalGameWorld game)
        {
            Base = baseAddr;
            _game = game;
            var mainPlayer = Memory.ReadPtr(_game + Offsets.ClientLocalGameWorld.MainPlayer, false);
            var localPlayer = new LocalPlayer(mainPlayer);
            _players[localPlayer] = LocalPlayer = localPlayer;
        }

        #endregion

        /// <summary>
        /// Updates the ConcurrentDictionary of 'Players'
        /// </summary>
        public void Refresh()
        {
            try
            {
                using var playersList = MemList<ulong>.Get(this, false); // Realtime Read
                // Reuse the pre-allocated scratch set to avoid a per-tick HashSet allocation.
                _registeredScratch.Clear();
                foreach (var addr in playersList)
                    if (addr != 0x0)
                        _registeredScratch.Add(addr);
                var registered = _registeredScratch;
                int i = -1;
                // Allocate New Players
                foreach (var playerBase in registered)
                {
                    if (playerBase == LocalPlayer) // Skip LocalPlayer, already allocated
                        continue;
                    i++;
                    if (_players.TryGetValue(playerBase, out var existingPlayer)) // Player already exists
                    {
                        if (existingPlayer.ErrorTimer.ElapsedMilliseconds >= 1500) // Erroring out a lot? Re-Alloc
                        {
                            Log.WriteLine($"WARNING - Existing player '{existingPlayer.Name}' being re-allocated due to excessive errors...");
                            _ = Player.Allocate(_players, playerBase); // Ignore result, already in dict
                        }
                        // Nothing else needs to happen here
                    }
                    else // Add New Player
                    {
                        // Check if we should skip due to repeated failures
                        if (_failedAllocations.TryGetValue(playerBase, out var failInfo))
                        {
                            // If we've failed too many times, wait for cooldown before retrying
                            if (failInfo.Count >= MAX_FAIL_COUNT &&
                                DateTime.UtcNow - failInfo.LastAttempt < FAIL_RETRY_COOLDOWN)
                                continue;
                        }

                        if (Player.Allocate(_players, playerBase))
                        {
                            _failedAllocations.TryRemove(playerBase, out _); // Clear fail count on success
                            if (_players.TryGetValue(playerBase, out var newPlayer))
                                newPlayer.ListIndex = i;
                            Log.Write(AppLogLevel.Info, $"New Player Allocated: {i} - {playerBase:X}", "Players");
                        }
                        else
                        {
                            // Track failure with timestamp
                            _failedAllocations.AddOrUpdate(playerBase,
                                (1, DateTime.UtcNow),
                                (_, old) => (old.Count + 1, DateTime.UtcNow));
                        }
                    }
                }

                // Update Existing Players including LocalPlayer
                UpdateExistingPlayers(registered);
                HandleBtrStickiness();
                // Clean up failed allocation tracking for addresses no longer in the game.
                // Only run every 64 refreshes — entries are small and the list changes rarely.
                if ((_refreshTick++ & 0x3F) == 0)
                {
                    foreach (var kvp in _failedAllocations)
                    {
                        if (!registered.Contains(kvp.Key))
                            _failedAllocations.TryRemove(kvp.Key, out _);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"CRITICAL ERROR - RegisteredPlayers Loop FAILED: {ex}");
            }
        }
        private void HandleBtrStickiness()
        {
            // Fast-path: no BTR operators on this map (the common case)
            // Avoid LINQ + ToList() allocation when there are no BTRs at all
            bool hasBtr = false;
            foreach (var p in _players.Values)
                if (p is BtrOperator) { hasBtr = true; break; }
            if (!hasBtr)
                return;

            // Collect BTR positions (rare path — only when a BTR is present)
            var btrs = new List<Vector3>(4);
            foreach (var p in _players.Values)
                if (p is BtrOperator btr)
                    btrs.Add(btr.Position);

            foreach (var player in _players.Values)
            {
                // Skip BTR entities themselves
                if (player is BtrOperator)
                    continue;

                bool isLocal = player is LocalPlayer;
                bool isObservedHuman =
                    player is ObservedPlayer op && op.IsHuman;

                // Only humans + LocalPlayer are eligible
                if (!isLocal && !isObservedHuman)
                {
                    player.BtrStickTicks = 0;
                    player.BtrStaticRotationTicks = 0;
                    continue;
                }

                // Check if player is sitting on a BTR
                bool nearBtr = false;
                foreach (var btrPos in btrs)
                {
                    if (NearlyEqual(player.Position, btrPos))
                    {
                        nearBtr = true;
                        break;
                    }
                }

                if (!nearBtr)
                {
                    // Fully free again
                    player.BtrStickTicks = 0;
                    player.BtrStaticRotationTicks = 0;
                    continue;
                }

                // ---- Rotation logic (MapRotation) ----
                float currentRot = player.MapRotation;

                if (MapRotationNearlyEqual(currentRot, player.LastBtrMapRotation))
                {
                    // Rotation is stable → legit BTR passenger
                    player.BtrStaticRotationTicks++;
                }
                else
                {
                    // Rotation changed → suspicious
                    player.BtrStaticRotationTicks = 0;
                }

                player.LastBtrMapRotation = currentRot;
                player.BtrStickTicks++;

                // ---- Decision gate ----
                // Only reset if:
                // - stuck long enough
                // - rotation is NOT static
                if (player.BtrStickTicks >= 30 &&
                    player.BtrStaticRotationTicks < 5)
                {
                    if (isLocal)
                    {
                        Log.WriteRateLimited(
                            AppLogLevel.Warning,
                            "btr_fix_local",
                            TimeSpan.FromSeconds(5),
                            "LocalPlayer stuck to BTR with rotating view → soft reset",
                            "BTR FIX");

                        player.BtrStickTicks = 0;
                        player.BtrStaticRotationTicks = 0;
                        player.SoftResetRuntimeState();
                    }
                    else
                    {
                        Log.WriteRateLimited(
                            AppLogLevel.Warning,
                            $"btr_fix_{player.Base:X}",
                            TimeSpan.FromSeconds(5),
                            $"Stuck player {player.Name} rotating at BTR → soft reset",
                            "BTR FIX");

                        player.BtrStickTicks = 0;
                        player.BtrStaticRotationTicks = 0;
                        player.SoftResetRuntimeState();
                    }
                }
            }
        }

        private static bool MapRotationNearlyEqual(float a, float b, float eps = 0.75f)
        {
            float diff = MathF.Abs(a - b);
            if (diff > 180f)
                diff = 360f - diff;

            return diff <= eps;
        }

        /// <summary>
        /// Clears all tracked failed allocations (call on raid end).
        /// </summary>
        public void ClearFailedAllocations()
        {
            _failedAllocations.Clear();
        }
        public static Vector3 RotationToDirection(Vector2 rotation)
        {
            // Convert rotation (yaw, pitch) to a direction vector
            // This might need adjustments based on how you define rotation
            var yaw = (float)rotation.X.ToRadians();
            var pitch = (float)rotation.Y.ToRadians();
            Vector3 direction;
            direction.X = (float)(Math.Cos(pitch) * Math.Sin(yaw));
            direction.Y = (float)Math.Sin(-pitch); // Negative pitch because in Unity, as pitch increases, we look down
            direction.Z = (float)(Math.Cos(pitch) * Math.Cos(yaw));

            return Vector3.Normalize(direction);
        }
        /// <summary>
        /// Returns the Player Count currently in the Registered Players List.
        /// </summary>
        /// <returns>Count of players.</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public int GetPlayerCount()
        {
            var count = Memory.ReadValue<int>(this + MemList<byte>.CountOffset, false);
            if (count < 0 || count > 256)
                throw new ArgumentOutOfRangeException(nameof(count));
            return count;
        }
        private static bool NearlyEqual(Vector3 a, Vector3 b, float eps = 4.0f)
        {
            return Vector3.DistanceSquared(a, b) <= eps * eps;
        }
        /// <summary>
        /// Scans the existing player list and updates Players as needed.
        /// </summary>
        private void UpdateExistingPlayers(IReadOnlySet<ulong> registered)
        {
            var allPlayers = _players.Values;
            if (allPlayers.Count == 0)
                return;
            using var map = ScatterReadMap.Get();
            var round1 = map.AddRound(false);
            int i = 0;
            foreach (var player in allPlayers)
            {
                player.OnRegRefresh(round1[i++], registered);
            }
            map.Execute();
        }

        /// <summary>
        /// Checks if there is an existing BTR player in the Players Dictionary, and if not, it is allocated and swapped.
        /// </summary>
        /// <param name="btrPlayerBase">Player Base Addr for BTR Operator.</param>
        public void TryAllocateBTR(ulong btrView, ulong btrPlayerBase)
        {
            if (!_players.TryGetValue(btrPlayerBase, out var existing))
                return;

            // 🚫 NEVER convert real players into BTR operators
            if (existing.IsHuman || existing is LocalPlayer)
                return;

            // 🚫 Already a BTR
            if (existing is BtrOperator)
                return;

            // Only AI-controlled BTR gunners should be allowed
            var btr = new BtrOperator(btrView, btrPlayerBase);
            _players[btrPlayerBase] = btr;

            Log.WriteLine("BTR AI operator allocated");
        }

        #region IReadOnlyCollection
        public int Count => _players.Values.Count;
        public IEnumerator<Player> GetEnumerator() =>
            _players.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        #endregion
    }
}
