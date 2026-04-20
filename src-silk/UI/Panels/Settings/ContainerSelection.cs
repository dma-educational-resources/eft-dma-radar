using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static partial class SettingsPanel
    {
        // ── Container Selection ─────────────────────────────────────────────

        /// <summary>
        /// Unique container types from AllContainers, sorted by name.
        /// Built once (lazy), keyed by ShortName to deduplicate display names.
        /// </summary>
        private static (string Name, string Id)[]? _containerEntries;
        private static string _containerFilter = string.Empty;

        private static (string Name, string Id)[] GetContainerEntries()
        {
            if (_containerEntries is not null)
                return _containerEntries;

            // Deduplicate by ShortName — take first BSG ID per unique name
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<(string Name, string Id)>();

            foreach (var kvp in EftDataManager.AllContainers)
            {
                var item = kvp.Value;
                if (seen.Add(item.ShortName))
                    entries.Add((item.ShortName, item.BsgId));
            }

            entries.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _containerEntries = [.. entries];
            return _containerEntries;
        }

        private static void DrawContainerSelection()
        {
            var entries = GetContainerEntries();
            if (entries.Length == 0)
                return;

            var selected = Config.SelectedContainers;
            int selectedCount = 0;
            foreach (var (_, id) in entries)
            {
                if (selected.Contains(id))
                    selectedCount++;
            }

            // Select All / Deselect All toggle
            bool allSelected = selectedCount == entries.Length;
            bool noneSelected = selectedCount == 0;

            if (allSelected)
            {
                // Show as checked — clicking deselects all
                bool allVal = true;
                if (ImGui.Checkbox("Select All Containers", ref allVal) && !allVal)
                {
                    selected.Clear();
                    Config.MarkDirty();
                }
            }
            else
            {
                // Mixed or none — clicking selects all
                bool mixedVal = !noneSelected; // Will show unchecked if none, or we handle below
                if (!noneSelected)
                {
                    // Push a mixed-state visual hint (dim the check mark area)
                    ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(0.6f, 0.6f, 0.6f, 1f));
                }

                if (ImGui.Checkbox("Select All Containers", ref mixedVal))
                {
                    selected.Clear();
                    foreach (var (_, id) in entries)
                        selected.Add(id);
                    Config.MarkDirty();
                }

                if (!noneSelected)
                    ImGui.PopStyleColor();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{selectedCount}/{entries.Length} container types selected");

            // Search filter
            ImGui.SetNextItemWidth(180);
            ImGui.InputTextWithHint("##containerFilter", "Filter...", ref _containerFilter, 64);
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), $"({selectedCount}/{entries.Length})");

            // Scrollable list of container checkboxes
            float listHeight = Math.Min(entries.Length * ImGui.GetTextLineHeightWithSpacing(), 200f);
            if (ImGui.BeginChild("ContainerList", new Vector2(0, listHeight), ImGuiChildFlags.Borders))
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    var (name, id) = entries[i];

                    // Apply search filter
                    if (_containerFilter.Length > 0
                        && !name.Contains(_containerFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool isSelected = selected.Contains(id);
                    if (ImGui.Checkbox($"{name}##cnt_{i}", ref isSelected))
                    {
                        if (isSelected)
                        {
                            if (!selected.Contains(id))
                                selected.Add(id);
                        }
                        else
                        {
                            selected.Remove(id);
                        }
                        Config.MarkDirty();
                    }
                }
            }
            ImGui.EndChild();
        }
    }
}
