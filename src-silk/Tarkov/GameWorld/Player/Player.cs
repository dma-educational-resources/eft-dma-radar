using System.Collections.Frozen;
using eft_dma_radar.Silk.Tarkov;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player
{
    /// <summary>
    /// Player data model — state, classification, and position.
    /// Rendering is in <c>Player.Draw.cs</c>.
    /// </summary>
    public partial class Player
    {
        /// <summary>Player display name (in-game nickname or AI template name).</summary>
        public string Name { get; set; } = string.Empty;

        private PlayerType _type;

        /// <summary>
        /// Player type classification. Setting this also updates <see cref="DrawPriority"/>.
        /// </summary>
        public PlayerType Type
        {
            get => _type;
            set
            {
                _type = value;
                DrawPriority = value switch
                {
                    PlayerType.SpecialPlayer => 7,
                    PlayerType.USEC or PlayerType.BEAR => 5,
                    PlayerType.PScav => 4,
                    PlayerType.AIBoss => 3,
                    PlayerType.AIRaider => 2,
                    _ => 1
                };
            }
        }

        /// <summary>World position updated each realtime tick via DMA scatter read.</summary>
        public Vector3 Position { get; set; }

        /// <summary>
        /// True after the first successful position read from DMA.
        /// Players with HasValidPosition=false are not rendered on the radar.
        /// </summary>
        public bool HasValidPosition { get; set; }

        private float _rotationYaw;
        /// <summary>
        /// Player yaw in degrees [0..360].
        /// Setting this also pre-computes <see cref="MapRotation"/>.
        /// </summary>
        public float RotationYaw
        {
            get => _rotationYaw;
            set
            {
                _rotationYaw = value;
                float mapRot = value - 90f;
                MapRotation = ((mapRot % 360f) + 360f) % 360f;
            }
        }

        /// <summary>
        /// Pre-computed map rotation (yaw - 90°, normalized).
        /// </summary>
        public float MapRotation { get; private set; }

        /// <summary>BSG group ID (party/squad). Players in the same group are teammates. -1 = unknown.</summary>
        public int GroupID { get; set; } = -1;

        /// <summary>Position-based spawn group ID assigned at first sighting. -1 = unassigned.</summary>
        public int SpawnGroupID { get; set; } = -1;

        /// <summary>Whether this player is alive (false after death).</summary>
        public bool IsAlive { get; set; } = true;

        /// <summary>Whether this player is actively tracked (false = no longer in registered players).</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>Whether this player is in a DMA error state (transform read failures).</summary>
        public bool IsError { get; set; }

        /// <summary>Whether this player is the local (MainPlayer) player.</summary>
        public virtual bool IsLocalPlayer => false;

        /// <summary>Whether this player is a human-controlled PMC or player scav.</summary>
        public bool IsHuman => Type is PlayerType.Default or PlayerType.Teammate
            or PlayerType.USEC or PlayerType.BEAR or PlayerType.PScav
            or PlayerType.Streamer or PlayerType.SpecialPlayer;

        /// <summary>Whether this player is a hostile human (PMC/PScav, not a teammate).</summary>
        public bool IsHostile => IsHuman && Type is not PlayerType.Teammate;

        /// <summary>
        /// Draw priority for Z-ordering on the radar. Higher = drawn later (on top).
        /// Cached on <see cref="Type"/> assignment to avoid per-sort switch overhead.
        /// </summary>
        public int DrawPriority { get; private set; } = 1;

        #region Identity (Dogtag)

        /// <summary>BSG Profile ID resolved from the player's alive dogtag during gear refresh.</summary>
        public string? ProfileId { get; set; }

        /// <summary>BSG Account ID resolved from corpse dogtag cache.</summary>
        public string? AccountId { get; set; }

        /// <summary>Player level resolved from corpse dogtag cache.</summary>
        public int Level { get; set; }

        #endregion

        #region Profile (tarkov.dev)

        /// <summary>Cached profile data from tarkov.dev. Null if not yet fetched or unavailable.</summary>
        internal ProfileService.ProfileData? Profile { get; set; }

        #endregion

        #region Gear

        /// <summary>Equipment slots keyed by slot name (e.g. "FirstPrimaryWeapon", "Headwear").</summary>
        internal IReadOnlyDictionary<string, GearItem> Equipment { get; set; } = FrozenDictionary<string, GearItem>.Empty;

        /// <summary>Total estimated gear value in roubles.</summary>
        public int GearValue { get; set; }

        /// <summary>Whether this player has night vision goggles equipped.</summary>
        public bool HasNVG { get; set; }

        /// <summary>Whether this player has a thermal scope/device equipped.</summary>
        public bool HasThermal { get; set; }

        /// <summary>Whether gear has been read at least once for this player.</summary>
        public bool GearReady { get; set; }

        #endregion

        public override string ToString() => $"{Type} [{Name}]";
    }
}
