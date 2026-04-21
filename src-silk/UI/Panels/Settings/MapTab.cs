using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static partial class SettingsPanel
    {
        private static void DrawMapTab()
        {
            if (!ImGui.BeginTabItem("Map"))
                return;

            ImGui.Spacing();

            ImGui.SetNextItemWidth(200);
            int zoom = RadarWindow.Zoom;
            if (ImGui.SliderInt("Zoom", ref zoom, 1, 200))
                RadarWindow.Zoom = zoom;

            bool freeMode = RadarWindow.FreeMode;
            if (ImGui.Checkbox("Free Mode", ref freeMode))
                RadarWindow.FreeMode = freeMode;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Toggle between player-follow and free-pan  [F]");

            ImGui.Spacing();
            ImGui.SeparatorText("Corpses");

            bool showCorpses = Config.ShowCorpses;
            if (ImGui.Checkbox("Show Corpses", ref showCorpses))
                Config.ShowCorpses = showCorpses;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show corpse X markers on the radar");

            ImGui.Spacing();
            ImGui.SeparatorText("Loot Markers");

            float dotSize = Config.LootDotSize;
            ImGui.SetNextItemWidth(120);
            if (ImGui.SliderFloat("Dot Size", ref dotSize, 1.5f, 8f, "%.1f px"))
                Config.LootDotSize = dotSize;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Base radius of loot dots. Tier/important bumps are added on top.");

            float labelFont = Config.LootLabelFontSize;
            ImGui.SetNextItemWidth(120);
            if (ImGui.SliderFloat("Label Font", ref labelFont, 8f, 22f, "%.0f px"))
                Config.LootLabelFontSize = labelFont;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Font size of loot labels on the radar.");

            bool heightArrows = Config.LootShowHeightArrows;
            if (ImGui.Checkbox("Height Arrows (▲/▼)", ref heightArrows))
                Config.LootShowHeightArrows = heightArrows;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show an up/down arrow on loot that is above or below your floor.");

            if (Config.LootShowHeightArrows)
            {
                ImGui.Indent(16);
                float thr = Config.LootHeightArrowThreshold;
                ImGui.SetNextItemWidth(120);
                if (ImGui.SliderFloat("Height Threshold", ref thr, 0.5f, 5f, "%.1f m"))
                    Config.LootHeightArrowThreshold = thr;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Vertical distance (±m) before an arrow is drawn.");

                bool showDelta = Config.LootShowHeightDelta;
                if (ImGui.Checkbox("Show Height (+/-m)", ref showDelta))
                    Config.LootShowHeightDelta = showDelta;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Append the exact vertical offset in meters to the loot label.");
                ImGui.Unindent(16);
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Containers");

            bool showContainers = Config.ShowContainers;
            if (ImGui.Checkbox("Show Containers", ref showContainers))
                Config.ShowContainers = showContainers;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show static loot containers on the radar (duffle bags, toolboxes, etc.)");

            if (Config.ShowContainers)
            {
                ImGui.Indent(16);
                bool showContainerNames = Config.ShowContainerNames;
                if (ImGui.Checkbox("Show Names", ref showContainerNames))
                    Config.ShowContainerNames = showContainerNames;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show container name labels next to markers");

                bool hideSearched = Config.HideSearchedContainers;
                if (ImGui.Checkbox("Hide Searched", ref hideSearched))
                    Config.HideSearchedContainers = hideSearched;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Hide containers that have been opened/searched");

                ImGui.Spacing();
                DrawContainerSelection();
                ImGui.Unindent(16);
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Exfils");

            bool showExfils = Config.ShowExfils;
            if (ImGui.Checkbox("Show Exfils", ref showExfils))
                Config.ShowExfils = showExfils;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show exfiltration points on the radar");

            if (Config.ShowExfils)
            {
                ImGui.Indent(16);

                bool hideInactive = Config.HideInactiveExfils;
                if (ImGui.Checkbox("Hide Inactive", ref hideInactive))
                    Config.HideInactiveExfils = hideInactive;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Hide closed or unavailable exfils");

                ImGui.Unindent(16);
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Transits");

            bool showTransits = Config.ShowTransits;
            if (ImGui.Checkbox("Show Transits", ref showTransits))
                Config.ShowTransits = showTransits;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show transit points (map-to-map travel) on the radar");

            ImGui.Spacing();
            ImGui.SeparatorText("Doors");

            bool showDoors = Config.ShowDoors;
            if (ImGui.Checkbox("Show Doors", ref showDoors))
                Config.ShowDoors = showDoors;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show keyed doors on the radar");

            if (Config.ShowDoors)
            {
                ImGui.Indent(16);

                bool showLocked = Config.ShowLockedDoors;
                if (ImGui.Checkbox("Show Locked", ref showLocked))
                    Config.ShowLockedDoors = showLocked;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show locked doors (red)");

                bool showUnlocked = Config.ShowUnlockedDoors;
                if (ImGui.Checkbox("Show Unlocked", ref showUnlocked))
                    Config.ShowUnlockedDoors = showUnlocked;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show open or shut doors (green/orange)");

                bool onlyNearLoot = Config.DoorsOnlyNearLoot;
                if (ImGui.Checkbox("Only Near Valuable Loot", ref onlyNearLoot))
                    Config.DoorsOnlyNearLoot = onlyNearLoot;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Only show doors near important (high-value) loot items");

                if (Config.DoorsOnlyNearLoot)
                {
                    ImGui.Indent(16);

                    ImGui.SetNextItemWidth(160);
                    float proximity = Config.DoorLootProximity;
                    if (ImGui.SliderFloat("Proximity (m)", ref proximity, 5f, 100f, "%.0fm"))
                        Config.DoorLootProximity = proximity;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Max distance from door to valuable loot");

                    ImGui.Unindent(16);
                }

                ImGui.Unindent(16);
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Quests");

            bool showQuests = Config.ShowQuests;
            if (ImGui.Checkbox("Show Quest Zones", ref showQuests))
                Config.ShowQuests = showQuests;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show quest objective zones on the radar");

            if (Config.ShowQuests)
            {
                ImGui.Indent(16);

                bool kappaFilter = Config.QuestKappaFilter;
                if (ImGui.Checkbox("Kappa Only", ref kappaFilter))
                    Config.QuestKappaFilter = kappaFilter;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Only show quests required for the Kappa container");

                bool showOptional = Config.QuestShowOptional;
                if (ImGui.Checkbox("Show Optional", ref showOptional))
                    Config.QuestShowOptional = showOptional;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show optional quest objectives");

                bool showNames = Config.QuestShowNames;
                if (ImGui.Checkbox("Show Names", ref showNames))
                    Config.QuestShowNames = showNames;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show quest names next to zone markers");

                bool showDistance = Config.QuestShowDistance;
                if (ImGui.Checkbox("Show Distance", ref showDistance))
                    Config.QuestShowDistance = showDistance;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show distance to quest zones");

                ImGui.Unindent(16);
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Explosives & BTR");

            bool showExplosives = Config.ShowExplosives;
            if (ImGui.Checkbox("Show Explosives", ref showExplosives))
                Config.ShowExplosives = showExplosives;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show grenades, tripwires, and mortar projectiles on the radar");

            if (Config.ShowExplosives)
            {
                ImGui.Indent(16);

                bool showTripwireLines = Config.ShowTripwireLines;
                if (ImGui.Checkbox("Show Tripwire Lines", ref showTripwireLines))
                    Config.ShowTripwireLines = showTripwireLines;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Draw a line between tripwire endpoints");

                ImGui.Unindent(16);
            }

            bool showBtr = Config.ShowBTR;
            if (ImGui.Checkbox("Show BTR", ref showBtr))
                Config.ShowBTR = showBtr;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show the BTR armored vehicle on the radar (Streets/Woods)");

            ImGui.Separator();
            ImGui.TextDisabled("Killfeed Overlay");

            bool showKf = Config.ShowKillFeed;
            if (ImGui.Checkbox("Show Killfeed Overlay", ref showKf))
                Config.ShowKillFeed = showKf;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Draw kill events on the radar canvas (top-right corner).\nOpen the Killfeed panel (\u2620) for the full table and settings.");

            if (Config.ShowKillFeed)
            {
                ImGui.Indent(16);

                int maxEnt = Config.KillFeedMaxEntries;
                ImGui.SetNextItemWidth(80);
                if (ImGui.SliderInt("Max Entries", ref maxEnt, 1, 10))
                    Config.KillFeedMaxEntries = maxEnt;

                int ttl = Config.KillFeedTtlSeconds;
                ImGui.SetNextItemWidth(80);
                if (ImGui.SliderInt("Entry TTL (s)", ref ttl, 5, 600))
                    Config.KillFeedTtlSeconds = ttl;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Seconds before a killfeed entry fades out (5–600).");

                if (ImGui.Button("Reset Killfeed Position"))
                {
                    Config.KillFeedPosX = -1f;
                    Config.KillFeedPosY = -1f;
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Snap the killfeed overlay back to the top-right corner.");

                ImGui.Unindent(16);
            }

            ImGui.EndTabItem();
        }
    }
}
