using ImGuiNET;

#nullable enable
namespace eft_dma_radar.Silk.UI.Widgets
{
    internal static class PlayerInfoWidget
    {
        private const float MIN_HEIGHT = 200f;
        private const float MAX_HEIGHT = 800f;

        // Reusable list — avoids per-frame allocation
        private static readonly List<Player> _hostilePlayers = new(32);

        /// <summary>Whether the player info widget is open.</summary>
        public static bool IsOpen { get; set; } = true;

        /// <summary>Draw the player info widget.</summary>
        public static void Draw()
        {
            var localPlayer = Memory.LocalPlayer;
            var allPlayers = Memory.Players;
            if (localPlayer is null || allPlayers is null)
                return;

            bool isOpen = IsOpen;
            ImGui.SetNextWindowSize(new Vector2(460, 350), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(320, MIN_HEIGHT), new Vector2(700, MAX_HEIGHT));

            if (!ImGui.Begin("Players", ref isOpen, ImGuiWindowFlags.NoCollapse))
            {
                IsOpen = isOpen;
                ImGui.End();
                return;
            }
            IsOpen = isOpen;

            var localPos = localPlayer.Position;

            // One-pass build: count + collect human hostiles
            _hostilePlayers.Clear();
            int pmcCount = 0, pscavCount = 0, aiCount = 0, bossCount = 0;

            foreach (var p in allPlayers)
            {
                if (p.IsLocalPlayer || !p.IsActive || !p.IsAlive || !p.HasValidPosition)
                    continue;

                switch (p.Type)
                {
                    case PlayerType.USEC or PlayerType.BEAR: pmcCount++; break;
                    case PlayerType.PScav: pscavCount++; break;
                    case PlayerType.AIBoss: bossCount++; break;
                    case PlayerType.AIScav or PlayerType.AIRaider: aiCount++; break;
                }

                if (p.IsHuman && p.IsHostile)
                    _hostilePlayers.Add(p);
            }

            _hostilePlayers.Sort((a, b) =>
                Vector3.DistanceSquared(localPos, a.Position).CompareTo(Vector3.DistanceSquared(localPos, b.Position)));

            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f),
                $"PMC: {pmcCount}  PScav: {pscavCount}  AI: {aiCount}  Boss: {bossCount}");
            ImGui.Separator();

            if (_hostilePlayers.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No human hostiles detected");
                ImGui.End();
                return;
            }

            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 2));

            var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                             ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY |
                             ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX;

            if (ImGui.BeginTable("PlayersTable", 3, tableFlags))
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 160f);
                ImGui.TableSetupColumn("Grp", ImGuiTableColumnFlags.WidthFixed, 40f);
                ImGui.TableSetupColumn("Dist", ImGuiTableColumnFlags.WidthFixed, 50f);
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();

                foreach (var player in _hostilePlayers)
                {
                    ImGui.TableNextRow();
                    var color = GetPlayerColor(player.Type);

                    ImGui.TableNextColumn();
                    ImGui.TextColored(color, $"{GetTypePrefix(player.Type)}{player.Name}");

                    ImGui.TableNextColumn();
                    ImGui.TextColored(color, player.SpawnGroupID == -1 ? "--" : player.SpawnGroupID.ToString());

                    ImGui.TableNextColumn();
                    ImGui.TextColored(color, ((int)Vector3.Distance(localPos, player.Position)).ToString());
                }

                ImGui.EndTable();
            }

            ImGui.PopStyleVar();
            ImGui.End();
        }

        private static string GetTypePrefix(PlayerType t) => t switch
        {
            PlayerType.USEC          => "[U] ",
            PlayerType.BEAR          => "[B] ",
            PlayerType.PScav         => "[PS] ",
            PlayerType.SpecialPlayer => "[!] ",
            PlayerType.Streamer      => "[TTV] ",
            _                        => ""
        };

        private static Vector4 GetPlayerColor(PlayerType t) => t switch
        {
            PlayerType.USEC or PlayerType.BEAR => new Vector4(0.38f, 0.55f, 1f, 1f),
            PlayerType.PScav                   => new Vector4(0.9f, 0.8f, 0.2f, 1f),
            PlayerType.SpecialPlayer           => new Vector4(1f, 0.4f, 0f, 1f),
            PlayerType.Streamer                => new Vector4(0.6f, 0.2f, 1f, 1f),
            _                                  => new Vector4(1f, 1f, 1f, 1f)
        };
    }
}
