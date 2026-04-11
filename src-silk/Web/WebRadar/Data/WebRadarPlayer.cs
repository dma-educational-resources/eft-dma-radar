namespace eft_dma_radar.Silk.Web.WebRadar.Data
{
    /// <summary>
    /// Flattened player snapshot for the web radar client.
    /// All coordinates are world-space — the JS client computes map-space position.
    /// </summary>
    public sealed class WebRadarPlayer
    {
        public string Name { get; set; } = string.Empty;
        public WebPlayerType Type { get; set; }
        public bool IsActive { get; set; }
        public bool IsAlive { get; set; }
        public bool IsLocal { get; set; }
        public bool IsFriendly { get; set; }
        public bool IsHuman { get; set; }
        public int GroupId { get; set; }
        public int GearValue { get; set; }

        // World-space position
        public float WorldX { get; set; }
        public float WorldY { get; set; }
        public float WorldZ { get; set; }

        // Rotation (radians, pre-converted for JS canvas)
        public float Yaw { get; set; }

        /// <summary>
        /// Creates a web radar player snapshot from a Silk player instance.
        /// </summary>
        internal static WebRadarPlayer CreateFromPlayer(Player player)
        {
            var isLocal = player.IsLocalPlayer;

            var webType = isLocal ? WebPlayerType.LocalPlayer :
                player.Type switch
                {
                    PlayerType.Teammate => WebPlayerType.Teammate,
                    PlayerType.USEC or PlayerType.BEAR => WebPlayerType.Player,
                    PlayerType.PScav => WebPlayerType.PlayerScav,
                    PlayerType.AIBoss => WebPlayerType.Boss,
                    PlayerType.AIRaider => WebPlayerType.Raider,
                    _ => WebPlayerType.Bot
                };

            var pos = player.Position;
            var yawDeg = player.MapRotation;
            var yawRad = yawDeg * (MathF.PI / 180f);

            return new WebRadarPlayer
            {
                Name = player.Name,
                Type = webType,
                IsActive = player.IsActive,
                IsAlive = player.IsAlive,
                IsLocal = isLocal,
                IsFriendly = player.Type is PlayerType.Teammate,
                IsHuman = player.IsHuman,
                GroupId = player.SpawnGroupID,
                GearValue = player.GearValue,
                WorldX = pos.X,
                WorldY = pos.Y,
                WorldZ = pos.Z,
                Yaw = yawRad,
            };
        }
    }
}
