using System.Numerics;

namespace eft_dma_radar.Silk.UI
{
    /// <summary>
    /// Shared ImGui color palette. Centralizes the Vector4 color definitions
    /// that were previously duplicated across panels (ColGreen, ColRed, etc).
    /// All values are normalized (0..1) and fully opaque unless named otherwise.
    /// </summary>
    internal static class UITheme
    {
        // ── Status colors ───────────────────────────────────────────────────
        public static readonly Vector4 Green  = new(0.30f, 0.69f, 0.31f, 1f);
        public static readonly Vector4 Red    = new(0.94f, 0.33f, 0.31f, 1f);
        public static readonly Vector4 Orange = new(1.00f, 0.60f, 0.00f, 1f);
        public static readonly Vector4 Yellow = new(1.00f, 0.84f, 0.00f, 1f);
        public static readonly Vector4 Gold   = new(1.00f, 0.84f, 0.00f, 1f);
        public static readonly Vector4 Cyan   = new(0.00f, 0.80f, 0.80f, 1f);
        public static readonly Vector4 White  = new(1f, 1f, 1f, 1f);

        // ── Neutrals ────────────────────────────────────────────────────────
        public static readonly Vector4 Grey   = new(0.62f, 0.62f, 0.62f, 1f);
        public static readonly Vector4 Slate  = new(0.47f, 0.56f, 0.61f, 1f);
        public static readonly Vector4 Dim    = new(1f, 1f, 1f, 0.38f);

        // ── Accent (footer auto-save hint etc.) ─────────────────────────────
        public static readonly Vector4 AccentGreen = new(0.55f, 0.75f, 0.55f, 1f);
    }
}
