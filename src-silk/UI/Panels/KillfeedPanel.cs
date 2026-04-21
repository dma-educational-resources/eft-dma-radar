using System.Numerics;

using eft_dma_radar.Silk.Tarkov.GameWorld.Loot;
using eft_dma_radar.Silk.Tarkov.GameWorld.Player;

using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    /// <summary>
    /// ImGui panel showing the live killfeed — kill events sourced from corpse dogtags.
    /// Complements the radar canvas killfeed overlay.
    /// </summary>
    internal static class KillfeedPanel
    {
        private static SilkConfig Config => SilkProgram.Config;

        public static bool IsOpen { get; set; }

        // Colours
        private static readonly Vector4 ColTeammate = new(0.31f, 0.86f, 0.31f, 1f);
        private static readonly Vector4 ColUSEC     = new(0.90f, 0.24f, 0.24f, 1f);
        private static readonly Vector4 ColBEAR     = new(0.27f, 0.51f, 0.90f, 1f);
        private static readonly Vector4 ColPScav    = new(0.86f, 0.86f, 0.86f, 1f);
        private static readonly Vector4 ColDefault  = new(0.94f, 0.90f, 0.24f, 1f);
        private static readonly Vector4 ColGrey     = new(0.62f, 0.62f, 0.62f, 1f);
        private static readonly Vector4 ColWhite    = new(1f,    1f,    1f,    1f);
        private static readonly Vector4 ColGreen    = new(0.30f, 0.69f, 0.31f, 1f);

        public static void Draw()
        {
            bool isOpen = IsOpen;
            using var scope = PanelWindow.Begin("\u2620 Killfeed", ref isOpen, new Vector2(520, 320));
            IsOpen = isOpen;
            if (!scope.Visible)
                return;

            DrawToolbar();
            ImGui.Separator();

            var entries = KillfeedManager.Entries;
            if (entries.Length == 0)
            {
                ImGui.TextColored(ColGrey, "No kills detected yet this raid.");
                return;
            }

            DrawTable(entries);
        }

        private static void DrawToolbar()
        {
            bool showOverlay = Config.ShowKillFeed;
            if (ImGui.Checkbox("Radar Overlay", ref showOverlay))
            {
                Config.ShowKillFeed = showOverlay;
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Draw the killfeed overlay on the radar canvas.");

            ImGui.SameLine();

            int maxEntries = Config.KillFeedMaxEntries;
            ImGui.SetNextItemWidth(60);
            if (ImGui.InputInt("Max", ref maxEntries, 0))
            {
                Config.KillFeedMaxEntries = Math.Clamp(maxEntries, 1, 20);
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Maximum killfeed entries shown (1–20).");

            ImGui.SameLine();

            int ttl = Config.KillFeedTtlSeconds;
            ImGui.SetNextItemWidth(60);
            if (ImGui.InputInt("TTL (s)", ref ttl, 0))
            {
                Config.KillFeedTtlSeconds = Math.Clamp(ttl, 5, 600);
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Seconds before a killfeed entry expires.");

            ImGui.SameLine();
            if (ImGui.SmallButton("Clear"))
                KillfeedManager.Reset();
        }

        private static void DrawTable(KillfeedEntry[] entries)
        {
            ImGui.SetNextWindowContentSize(new Vector2(0, 0));
            const ImGuiTableFlags flags =
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.BordersInnerV |
                ImGuiTableFlags.SizingStretchProp |
                ImGuiTableFlags.ScrollY;

            if (!ImGui.BeginTable("killfeed_table", 5, flags, new Vector2(0, 0)))
                return;

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Age",    ImGuiTableColumnFlags.WidthFixed,   42f);
            ImGui.TableSetupColumn("Killer", ImGuiTableColumnFlags.WidthStretch, 1.2f);
            ImGui.TableSetupColumn("Victim", ImGuiTableColumnFlags.WidthStretch, 1.2f);
            ImGui.TableSetupColumn("Lvl",    ImGuiTableColumnFlags.WidthFixed,   34f);
            ImGui.TableSetupColumn("Weapon", ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableHeadersRow();

            var ttl = (double)Config.KillFeedTtlSeconds;

            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                double age = e.AgeSec;
                float alpha = ttl > 0 ? Math.Clamp(1f - (float)(age / ttl), 0.25f, 1f) : 1f;

                var nameCol = SideColor(e.KillerSide);
                var fadedCol = nameCol with { W = nameCol.W * alpha };
                var dimFaded = ColGrey with { W = alpha };

                ImGui.TableNextRow();

                // Age
                ImGui.TableSetColumnIndex(0);
                ImGui.TextColored(dimFaded, FormatAge(age));

                // Killer
                ImGui.TableSetColumnIndex(1);
                ImGui.TextColored(fadedCol, e.Killer);

                // Victim
                ImGui.TableSetColumnIndex(2);
                ImGui.TextColored(ColWhite with { W = alpha }, e.Victim);

                // Level
                ImGui.TableSetColumnIndex(3);
                if (e.VictimLevel > 0)
                    ImGui.TextColored(ColGreen with { W = alpha }, e.VictimLevel.ToString());

                // Weapon
                ImGui.TableSetColumnIndex(4);
                if (!string.IsNullOrWhiteSpace(e.Weapon))
                    ImGui.TextColored(dimFaded, e.Weapon);
            }

            ImGui.EndTable();
        }

        private static Vector4 SideColor(PlayerType side) => side switch
        {
            PlayerType.Teammate     => ColTeammate,
            PlayerType.USEC         => ColUSEC,
            PlayerType.BEAR         => ColBEAR,
            PlayerType.PScav        => ColPScav,
            _                       => ColDefault,
        };

        private static string FormatAge(double secs)
        {
            if (secs < 60)
                return $"{(int)secs}s";
            return $"{(int)(secs / 60)}m";
        }
    }
}
