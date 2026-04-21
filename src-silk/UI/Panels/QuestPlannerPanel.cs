using System.Numerics;

using eft_dma_radar.Silk.Tarkov.QuestPlanner;
using eft_dma_radar.Silk.Tarkov.QuestPlanner.Models;

using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    /// <summary>
    /// Quest Planner Panel — lobby-only view that recommends which map(s) to run next
    /// based on the player's active quests, unlock dependencies, and per-map bring lists.
    /// Consumes <see cref="QuestPlannerWorker.Current"/>.
    /// </summary>
    internal static class QuestPlannerPanel
    {
        private static SilkConfig Config => SilkProgram.Config;

        public static bool IsOpen { get; set; }

        // Collapsed sections
        private static readonly HashSet<string> _collapsedMaps = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _collapsedQuests = new(StringComparer.OrdinalIgnoreCase);
        private static bool _collapsedAllMaps = true;
        private static bool _collapsedFir;
        private static bool _collapsedHandOver;

        private static readonly Vector4 ColGreen    = new(0.30f, 0.69f, 0.31f, 1f);
        private static readonly Vector4 ColOrange   = new(1.00f, 0.60f, 0.00f, 1f);
        private static readonly Vector4 ColGrey     = new(0.62f, 0.62f, 0.62f, 1f);
        private static readonly Vector4 ColCyan     = new(0.00f, 0.80f, 0.80f, 1f);
        private static readonly Vector4 ColYellow   = new(1.00f, 0.84f, 0.00f, 1f);
        private static readonly Vector4 ColDim      = new(1f, 1f, 1f, 0.45f);
        private static readonly Vector4 ColWhite    = new(1f, 1f, 1f, 1f);
        private static readonly Vector4 ColBlue     = new(0.40f, 0.60f, 1.00f, 1f);

        public static void Draw()
        {
            bool isOpen = IsOpen;
            using var scope = PanelWindow.Begin("\u2741 Quest Planner", ref isOpen, new Vector2(560, 640));
            IsOpen = isOpen;
            if (!scope.Visible) return;

            DrawToolbar();
            ImGui.Separator();

            var state = QuestPlannerWorker.State;
            if (state == QuestPlannerState.Disconnected)
            {
                ImGui.TextColored(ColDim, "Game not connected.");
                return;
            }
            if (state == QuestPlannerState.InRaid)
            {
                ImGui.TextColored(ColDim, "In raid — planner suspended. Extract to refresh.");
                return;
            }

            var summary = QuestPlannerWorker.Current;
            if (summary is null)
            {
                ImGui.TextColored(ColDim, QuestPlannerWorker.IsStale
                    ? "Waiting for profile to become available…"
                    : "Computing plan…");
                return;
            }

            DrawHeader(summary);
            ImGui.Separator();
            DrawTraderBanners(summary);
            DrawHandOverSection(summary);
            DrawFirSection(summary);

            ImGui.Separator();
            DrawMapList(summary);
            DrawAllMapsSection(summary);
        }

        // ── Toolbar ──────────────────────────────────────────────────────────

        private static void DrawToolbar()
        {
            bool kappa = Config.QuestKappaFilter;
            if (ImGui.Checkbox("Kappa only", ref kappa))
            {
                Config.QuestKappaFilter = kappa;
                QuestPlannerWorker.ForceRecompute();
            }

            ImGui.SameLine();
            if (ImGui.Button("Refresh"))
                QuestPlannerWorker.ForceRecompute();

            if (QuestPlannerWorker.IsStale)
            {
                ImGui.SameLine();
                ImGui.TextColored(ColOrange, "(stale)");
            }
        }

        // ── Header ───────────────────────────────────────────────────────────

        private static void DrawHeader(QuestSummary s)
        {
            ImGui.TextColored(ColWhite,
                $"{s.TotalActiveQuests} active quests  —  {s.TotalCompletableObjectives} completable objectives  —  {s.Maps.Count} maps planned");
            ImGui.TextColored(ColDim, $"Computed {s.ComputedAt.ToLocalTime():HH:mm:ss}");
        }

        private static void DrawTraderBanners(QuestSummary s)
        {
            if (s.AvailableForStartTraders.Count > 0)
            {
                ImGui.TextColored(ColCyan, "Available to start:");
                ImGui.SameLine();
                ImGui.TextUnformatted(string.Join(", ", s.AvailableForStartTraders));
            }
            if (s.AvailableForFinishTraders.Count > 0)
            {
                ImGui.TextColored(ColGreen, "Ready to turn in:");
                ImGui.SameLine();
                ImGui.TextUnformatted(string.Join(", ", s.AvailableForFinishTraders));
            }
        }

        // ── Hand-over ────────────────────────────────────────────────────────

        private static void DrawHandOverSection(QuestSummary s)
        {
            if (s.HandOverItems.Count == 0) return;
            ImGui.Separator();
            if (ImGui.Selectable($"{(_collapsedHandOver ? "\u25B6" : "\u25BC")} Hand over items ({s.HandOverItems.Count})", false))
                _collapsedHandOver = !_collapsedHandOver;
            if (_collapsedHandOver) return;

            foreach (var h in s.HandOverItems)
            {
                ImGui.Bullet();
                ImGui.TextColored(ColYellow, h.QuestName);
                ImGui.SameLine();
                ImGui.TextUnformatted($"— {h.ItemShortName}");
            }
        }

        // ── FIR items ────────────────────────────────────────────────────────

        private static void DrawFirSection(QuestSummary s)
        {
            if (s.FirItems.Count == 0) return;
            ImGui.Separator();
            if (ImGui.Selectable($"{(_collapsedFir ? "\u25B6" : "\u25BC")} Find in raid ({s.FirItems.Count})", false))
                _collapsedFir = !_collapsedFir;
            if (_collapsedFir) return;

            foreach (var fir in s.FirItems)
            {
                ImGui.Bullet();
                ImGui.TextColored(ColYellow, fir.QuestName);
                ImGui.SameLine();
                ImGui.TextUnformatted($"— {fir.ItemShortName}");
                ImGui.SameLine();
                ImGui.TextColored(ColDim, fir.ProgressText);
            }
        }

        // ── Map list ─────────────────────────────────────────────────────────

        private static void DrawMapList(QuestSummary s)
        {
            if (s.Maps.Count == 0)
            {
                ImGui.TextColored(ColDim, "No maps with completable objectives.");
                return;
            }

            foreach (var map in s.Maps)
                DrawMap(map);
        }

        private static void DrawMap(MapPlan map)
        {
            bool collapsed = _collapsedMaps.Contains(map.MapId);
            var arrow = collapsed ? "\u25B6" : "\u25BC";
            var recommended = map.IsRecommended ? " \u2605" : string.Empty;
            var headerColor = map.IsRecommended ? ColGreen : ColWhite;

            var label = $"{arrow} {map.MapName}{recommended}  —  {map.ActiveQuestCount} quests, {map.CompletableObjectiveCount} objectives";
            ImGui.PushStyleColor(ImGuiCol.Text, headerColor);
            bool clicked = ImGui.Selectable(label, false);
            ImGui.PopStyleColor();

            if (clicked)
            {
                if (collapsed) _collapsedMaps.Remove(map.MapId);
                else _collapsedMaps.Add(map.MapId);
            }

            if (collapsed) return;

            ImGui.Indent();

            // Bring list
            if (map.FilteredBringList.Count > 0)
            {
                ImGui.TextColored(ColCyan, "Bring:");
                ImGui.Indent();
                foreach (var b in map.FilteredBringList)
                {
                    var alts = string.Join(" / ", b.Alternatives);
                    ImGui.Bullet();
                    if (b.Type == BringItemType.Key)
                        ImGui.TextColored(ColOrange, alts);
                    else
                        ImGui.TextUnformatted(alts);
                    if (b.Count > 1)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(ColDim, $"x{b.Count}");
                    }
                }
                ImGui.Unindent();
            }

            // Quests
            if (map.Quests.Count > 0)
            {
                ImGui.TextColored(ColCyan, "Quests:");
                ImGui.Indent();
                foreach (var q in map.Quests)
                    DrawQuest(map.MapId, q);
                ImGui.Unindent();
            }

            // Unlocks
            if (map.UnlockedQuests.Count > 0)
            {
                ImGui.TextColored(ColBlue, $"Unlocks ({map.UnlockedQuests.Count}):");
                ImGui.Indent();
                foreach (var u in map.UnlockedQuests)
                {
                    ImGui.Bullet();
                    ImGui.TextUnformatted(u.QuestName);
                    ImGui.SameLine();
                    ImGui.TextColored(ColDim, $"({u.MapName})");
                }
                ImGui.Unindent();
            }

            ImGui.Unindent();
            ImGui.Spacing();
        }

        private static void DrawQuest(string mapId, QuestPlan quest)
        {
            var key = mapId + "\u0001" + quest.QuestName;
            bool collapsed = _collapsedQuests.Contains(key);
            var arrow = collapsed ? "\u25B6" : "\u25BC";
            var label = $"{arrow} {quest.QuestName}  ({quest.Objectives.Count})";
            if (ImGui.Selectable(label, false))
            {
                if (collapsed) _collapsedQuests.Remove(key);
                else _collapsedQuests.Add(key);
            }
            if (collapsed) return;

            ImGui.Indent();
            foreach (var o in quest.Objectives)
            {
                ImGui.Bullet();
                ImGui.TextUnformatted(o.Description);
                if (o.HasProgress)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(ColDim, o.ProgressText);
                }
            }
            ImGui.Unindent();
        }

        // ── All Maps ─────────────────────────────────────────────────────────

        private static void DrawAllMapsSection(QuestSummary s)
        {
            if (s.AllMapsQuests.Count == 0) return;
            ImGui.Separator();
            if (ImGui.Selectable($"{(_collapsedAllMaps ? "\u25B6" : "\u25BC")} All Maps — quests without a specific location ({s.AllMapsQuests.Count})", false))
                _collapsedAllMaps = !_collapsedAllMaps;
            if (_collapsedAllMaps) return;

            foreach (var q in s.AllMapsQuests)
                DrawQuest("_allmaps", q);
        }
    }
}
