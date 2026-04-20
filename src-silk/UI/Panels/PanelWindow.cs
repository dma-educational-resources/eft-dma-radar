using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    /// <summary>
    /// Scoped helper that wraps <see cref="ImGui.Begin(string, ref bool, ImGuiWindowFlags)"/>
    /// so panel code doesn't have to hand-pair <c>Begin</c>/<c>End</c>.
    ///
    /// Usage:
    /// <code>
    /// using var scope = PanelWindow.Begin("\u2699 Settings", ref IsOpen, new Vector2(440, 520));
    /// if (!scope.Visible) return;
    /// // ... draw contents ...
    /// </code>
    /// <see cref="ImGui.End"/> is invoked exactly once when the returned
    /// <see cref="Scope"/> is disposed — even if the window is collapsed
    /// (ImGui requires a matching <c>End()</c> in that case too).
    /// </summary>
    internal static class PanelWindow
    {
        /// <summary>
        /// Opens a panel window. Call in a <c>using</c> statement so <see cref="ImGui.End"/>
        /// is guaranteed to run.
        /// </summary>
        /// <param name="title">ImGui window title (and identifier).</param>
        /// <param name="isOpen">Bound open flag — the window draws a close button that flips this.</param>
        /// <param name="defaultSize">First-use size hint (ignored on subsequent frames).</param>
        /// <param name="flags">Window flags (defaults to <see cref="ImGuiWindowFlags.NoCollapse"/>).</param>
        public static Scope Begin(
            string title,
            ref bool isOpen,
            Vector2 defaultSize,
            ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse)
        {
            ImGui.SetNextWindowSize(defaultSize, ImGuiCond.FirstUseEver);
            bool visible = ImGui.Begin(title, ref isOpen, flags);
            return new Scope(visible);
        }

        /// <summary>Disposable scope that always calls <see cref="ImGui.End"/>.</summary>
        internal readonly ref struct Scope
        {
            /// <summary>True when the window body should be drawn (not collapsed / culled).</summary>
            public readonly bool Visible;

            internal Scope(bool visible)
            {
                Visible = visible;
            }

            public void Dispose() => ImGui.End();
        }
    }
}
