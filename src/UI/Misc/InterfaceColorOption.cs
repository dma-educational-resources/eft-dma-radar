using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using eft_dma_radar.UI.Misc;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace eft_dma_radar.UI
{
    public enum InterfaceColorOption
    {
        AccentColor,
        RegionColor,
        SecondaryRegionColor,
        BorderColor,
        RadarBackground,
        FuserBackground
    }

    internal static class InterfaceColorOptions
    {
        #region Static Interfaces

        public static SKColor RadarBackgroundColor { get; set; } = SKColors.Black;
        public static SKColor FuserBackgroundColor { get; set; } = SKColors.Black;

        /// <summary>
        /// Load all Interface Color Config. Run once at start of application.
        /// </summary>
        internal static void LoadColors(Config config)
        {
            config.InterfaceColors ??= new Dictionary<InterfaceColorOption, string>();

            foreach (var defaultColor in GetDefaultColors())
                config.InterfaceColors.TryAdd(defaultColor.Key, defaultColor.Value);

            SetColors(config.InterfaceColors);
        }

        /// <summary>
        /// Returns all default color combinations for the interface.
        /// </summary>
        internal static Dictionary<InterfaceColorOption, string> GetDefaultColors() =>
            new()
            {
                [InterfaceColorOption.AccentColor] = "#0094FF",
                [InterfaceColorOption.RegionColor] = "#222222",
                [InterfaceColorOption.SecondaryRegionColor] = "#333333",
                [InterfaceColorOption.BorderColor] = "#444444",
                [InterfaceColorOption.RadarBackground] = "#000000",
                [InterfaceColorOption.FuserBackground] = "#000000",
            };

        /// <summary>
        /// Save all Interface Color Changes.
        /// </summary>
        internal static void SetColors(IReadOnlyDictionary<InterfaceColorOption, string> colors)
        {
            try
            {
                foreach (var color in colors)
                {
                    if (!TryParseColor(color.Value, out var wpfColor))
                        throw new Exception($"Invalid Color Value for {color.Key}!");

                    var brush = new SolidColorBrush(wpfColor);

                    switch (color.Key)
                    {
                        case InterfaceColorOption.AccentColor:
                            var app = Application.Current as App;
                            if (app != null)
                                app.UpdateAccent(brush);
                            break;

                        case InterfaceColorOption.RegionColor:
                            Application.Current.Resources["RegionBrush"] = brush;
                            break;

                        case InterfaceColorOption.SecondaryRegionColor:
                            Application.Current.Resources["SecondaryRegionBrush"] = brush;
                            break;

                        case InterfaceColorOption.BorderColor:
                            Application.Current.Resources["BorderBrush"] = brush;
                            break;

                        case InterfaceColorOption.RadarBackground:
                            if (SKColor.TryParse(color.Value, out var radarColor))
                                RadarBackgroundColor = radarColor;
                            break;

                        case InterfaceColorOption.FuserBackground:
                            if (SKColor.TryParse(color.Value, out var fuserColor))
                                FuserBackgroundColor = fuserColor;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("ERROR Setting Interface Colors", ex);
            }
        }

        /// <summary>
        /// Update a single interface color
        /// </summary>
        internal static void UpdateColor(Config config, InterfaceColorOption option, Color color)
        {
            var singleColor = new Dictionary<InterfaceColorOption, string>
            {
                [option] = color.ToString()
            };

            SetColors(singleColor);

            if (config.InterfaceColors == null)
                config.InterfaceColors = new Dictionary<InterfaceColorOption, string>();

            config.InterfaceColors[option] = color.ToString();
            config.Save();
        }

        /// <summary>
        /// Helper method to convert string color representation to WPF Color
        /// </summary>
        private static bool TryParseColor(string colorStr, out Color color)
        {
            try
            {
                var colorObj = ColorConverter.ConvertFromString(colorStr);

                if (colorObj != null)
                {
                    color = (Color)colorObj;
                    return true;
                }

                color = Colors.White;

                return false;
            }
            catch
            {
                color = Colors.White;

                return false;
            }
        }

        /// <summary>
        /// Maps interface tag to enum
        /// </summary>
        internal static bool TryGetColorOption(string tag, out InterfaceColorOption option)
        {
            switch (tag)
            {
                case "Interface.Accent":
                    option = InterfaceColorOption.AccentColor;
                    return true;

                case "Interface.Region":
                    option = InterfaceColorOption.RegionColor;
                    return true;

                case "Interface.SecondaryRegion":
                    option = InterfaceColorOption.SecondaryRegionColor;
                    return true;

                case "Interface.BorderColor":
                    option = InterfaceColorOption.BorderColor;
                    return true;

                case "Interface.RadarBackground":
                    option = InterfaceColorOption.RadarBackground;
                    return true;

                case "Interface.FuserBackground":
                    option = InterfaceColorOption.FuserBackground;
                    return true;

                default:
                    option = InterfaceColorOption.AccentColor;
                    return false;
            }
        }

        /// <summary>
        /// Maps enum to interface tag
        /// </summary>
        internal static string GetTagFromOption(InterfaceColorOption option)
        {
            switch (option)
            {
                case InterfaceColorOption.AccentColor:
                    return "Interface.Accent";

                case InterfaceColorOption.RegionColor:
                    return "Interface.Region";

                case InterfaceColorOption.SecondaryRegionColor:
                    return "Interface.SecondaryRegion";

                case InterfaceColorOption.BorderColor:
                    return "Interface.BorderColor";

                case InterfaceColorOption.RadarBackground:
                    return "Interface.RadarBackground";

                case InterfaceColorOption.FuserBackground:
                    return "Interface.FuserBackground";

                default:
                    return string.Empty;
            }
        }

        #endregion
    }
}