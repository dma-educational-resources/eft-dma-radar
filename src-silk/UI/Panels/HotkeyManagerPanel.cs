using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    /// <summary>
    /// Standalone ImGui panel for adding, removing, and managing hotkey bindings.
    /// WPF-style: action dropdown, key capture input, Toggle/OnKey mode, add/remove.
    /// </summary>
    internal static class HotkeyManagerPanel
    {
        /// <summary>Whether the hotkey manager panel is open.</summary>
        public static bool IsOpen { get; set; }

        // ── Add-Hotkey UI state ─────────────────────────────────────────────
        private static int _selectedActionIndex = -1;
        private static int _capturedVk = -1;
        private static int _selectedMode; // 0 = Toggle, 1 = OnKey
        private static bool _isCapturing;

        // ── Cached unbound action list ──────────────────────────────────────
        private static string[] _unboundNames = [];
        private static string[] _unboundIds = [];
        private static bool _listDirty = true;

        /// <summary>Mark the unbound action list as needing rebuild.</summary>
        public static void InvalidateActionList() => _listDirty = true;

        /// <summary>
        /// Draw the hotkey manager panel.
        /// </summary>
        public static void Draw()
        {
            bool isOpen = IsOpen;
            ImGui.SetNextWindowSize(new Vector2(460, 400), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("\u2328 Hotkeys", ref isOpen, ImGuiWindowFlags.NoCollapse))
            {
                IsOpen = isOpen;
                ImGui.End();
                return;
            }
            IsOpen = isOpen;

            ImGui.Spacing();

            if (!InputManager.IsReady)
            {
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f),
                    "\u26a0 Input manager not initialized.");
                ImGui.TextWrapped("Hotkeys require an active DMA connection. They will activate once a raid starts.");
                ImGui.End();
                return;
            }

            DrawAddSection();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawActiveHotkeys();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f),
                "Hotkeys work via DMA \u2014 they read the gaming PC's keyboard state.");

            ImGui.End();
        }

        /// <summary>
        /// Call from render loop — handles live key capture.
        /// </summary>
        public static void ProcessCapture()
        {
            if (!_isCapturing || HotkeyManager.CapturingActionId is null)
                return;

            if (HotkeyManager.TryCaptureKey(out int vk))
            {
                if (vk > 0)
                    _capturedVk = vk;

                _isCapturing = false;
            }
        }

        // ── Add Section ─────────────────────────────────────────────────────

        private static void DrawAddSection()
        {
            ImGui.SeparatorText("Add Hotkey");

            // Rebuild unbound list if needed
            if (_listDirty)
                RebuildUnboundList();

            if (_unboundNames.Length == 0)
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "All actions have hotkeys assigned.");
                return;
            }

            // 1. Action dropdown
            ImGui.SetNextItemWidth(200);
            if (ImGui.Combo("Action", ref _selectedActionIndex, _unboundNames, _unboundNames.Length))
            {
                // Reset capture when changing action
                _capturedVk = -1;
                _isCapturing = false;
                HotkeyManager.CapturingActionId = null;
            }
            if (_selectedActionIndex >= 0 && _selectedActionIndex < _unboundIds.Length)
            {
                var def = HotkeyManager.GetAction(_unboundIds[_selectedActionIndex]);
                if (def is not null && ImGui.IsItemHovered())
                    ImGui.SetTooltip(def.Tooltip);
            }

            // 2. Key capture button
            ImGui.SameLine();
            string keyLabel = _isCapturing
                ? "[ Press a key... ]"
                : _capturedVk > 0 ? VK.GetName(_capturedVk) : "(None)";

            bool wasCapturing = _isCapturing;
            if (wasCapturing)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.5f, 0.1f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.6f, 0.2f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.4f, 0.1f, 1f));
            }

            if (ImGui.Button(keyLabel, new Vector2(130, 0)))
            {
                if (_isCapturing)
                {
                    // Cancel capture
                    _isCapturing = false;
                    HotkeyManager.CapturingActionId = null;
                }
                else
                {
                    // Start capture
                    _isCapturing = true;
                    _capturedVk = -1;
                    HotkeyManager.CapturingActionId = "_panel_capture";
                }
            }

            if (wasCapturing)
                ImGui.PopStyleColor(3);

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_isCapturing ? "Press a key to bind, or Escape to cancel" : "Click to capture a key");

            // 3. Mode radio buttons
            ImGui.RadioButton("Toggle", ref _selectedMode, 0);
            ImGui.SameLine();
            ImGui.RadioButton("OnKey (Hold)", ref _selectedMode, 1);

            // 4. Add button
            bool canAdd = _selectedActionIndex >= 0
                && _selectedActionIndex < _unboundIds.Length
                && _capturedVk > 0
                && !_isCapturing;

            if (!canAdd)
                ImGui.BeginDisabled();

            if (ImGui.Button("\u2713 Add Hotkey", new Vector2(120, 0)) && canAdd)
            {
                var actionId = _unboundIds[_selectedActionIndex];
                var mode = _selectedMode == 0 ? HotkeyMode.Toggle : HotkeyMode.OnKey;
                HotkeyManager.AddOrUpdate(actionId, _capturedVk, mode);

                Log.WriteLine($"[HotkeyManager] Added '{actionId}' on {VK.GetName(_capturedVk)} ({mode})");

                // Reset state
                _capturedVk = -1;
                _selectedActionIndex = -1;
                _selectedMode = 0;
                _listDirty = true;
            }

            if (!canAdd)
                ImGui.EndDisabled();
        }

        // ── Active Hotkeys Table ────────────────────────────────────────────

        private static void DrawActiveHotkeys()
        {
            ImGui.SeparatorText("Active Hotkeys");

            var hotkeys = SilkProgram.Config.Hotkeys;
            if (hotkeys.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "No hotkeys configured.");
                return;
            }

            if (ImGui.BeginTable("HotkeyTable", 5,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.None, 3f);
                ImGui.TableSetupColumn("Key", ImGuiTableColumnFlags.None, 2f);
                ImGui.TableSetupColumn("Mode", ImGuiTableColumnFlags.None, 1.5f);
                ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.None, 2f);
                ImGui.TableSetupColumn("##remove", ImGuiTableColumnFlags.WidthFixed, 24f);
                ImGui.TableHeadersRow();

                string? toRemove = null;

                foreach (var (id, entry) in hotkeys)
                {
                    if (!entry.Enabled || entry.Key < 1)
                        continue;

                    var def = HotkeyManager.GetAction(id);
                    string displayName = def?.DisplayName ?? id;
                    string category = def?.Category ?? "—";

                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    ImGui.Text(displayName);
                    if (def is not null && ImGui.IsItemHovered())
                        ImGui.SetTooltip(def.Tooltip);

                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0.9f, 0.8f, 0.3f, 1f), VK.GetName(entry.Key));

                    ImGui.TableNextColumn();
                    ImGui.Text(entry.Mode == HotkeyMode.Toggle ? "Toggle" : "OnKey");

                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0.5f, 0.7f, 0.9f, 1f), category);

                    ImGui.TableNextColumn();
                    if (ImGui.SmallButton($"\u2715##{id}"))
                        toRemove = id;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Remove this hotkey");
                }

                ImGui.EndTable();

                if (toRemove is not null)
                {
                    HotkeyManager.Remove(toRemove);
                    Log.WriteLine($"[HotkeyManager] Removed hotkey '{toRemove}'");
                    _listDirty = true;
                }
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static void RebuildUnboundList()
        {
            _listDirty = false;
            var hotkeys = SilkProgram.Config.Hotkeys;

            var names = new List<string>();
            var ids = new List<string>();

            foreach (var action in HotkeyManager.AvailableActions)
            {
                // Skip actions that already have an enabled binding
                if (hotkeys.TryGetValue(action.Id, out var entry) && entry.Enabled && entry.Key > 0)
                    continue;

                names.Add($"[{action.Category}] {action.DisplayName}");
                ids.Add(action.Id);
            }

            _unboundNames = [.. names];
            _unboundIds = [.. ids];

            // Clamp index if list shrank
            if (_selectedActionIndex >= _unboundNames.Length)
                _selectedActionIndex = _unboundNames.Length - 1;
        }
    }
}
