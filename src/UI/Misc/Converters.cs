using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using eft_dma_radar.Common.Misc;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace eft_dma_radar.Converters
{
    public class ColorHexToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hex && !string.IsNullOrWhiteSpace(hex))
            {
                try
                {
                    return (SolidColorBrush)(new BrushConverter().ConvertFrom(hex));
                }
                catch
                {
                    return Brushes.Transparent;
                }
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
                return brush.Color.ToString();

            return "#FFFFFFFF";
        }
    }

    public class ItemIconConverter : IValueConverter
    {
        private static readonly string IconPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar", "Assets", "Icons", "Items");

        public static async Task SaveItemIconAsPng(string itemId, string saveDir)
        {
            string webpUrl = $"https://assets.tarkov.dev/{itemId}-base-image.webp";
            string outputPath = Path.Combine(saveDir, $"{itemId}.png");

            // Don't re-download if a valid icon already exists
            if (File.Exists(outputPath))
            {
                var fileInfo = new FileInfo(outputPath);
                if (fileInfo.Length > 1024) // Skip re-download if file is >1KB (sanity check)
                    return;
            }

            try
            {
                using var httpClient = new HttpClient();
                var imageBytes = await httpClient.GetByteArrayAsync(webpUrl);

                using var memoryStream = new MemoryStream(imageBytes);
                using var codec = SKCodec.Create(memoryStream);
                var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888);
                using var bitmap = new SKBitmap(info);

                if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
                    throw new Exception("Failed to decode WebP image");

                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                data.SaveTo(outputStream);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IconCache] Error downloading icon for {itemId}: {ex.Message}");
            }
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string itemId) return null;

            string path = Path.Combine(IconPath, $"{itemId}.png");
            if (!File.Exists(path)) return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            //XMLogging.WriteLine($"[IconCache] Loaded icon for {itemId} from {path}");
            return image;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isEnabled)
                return isEnabled ? 1.0 : 0.1;

            return 0.4;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class Boolean2VisibilityConverter : IValueConverter
    {
        /// <summary>
        /// Gets or sets a value indicating whether to reverse the conversion logic.
        /// When true, true becomes Collapsed and false becomes Visible.
        /// </summary>
        public bool IsReversed { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to use Hidden instead of Collapsed.
        /// </summary>
        public bool UseHidden { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = false;

            if (value is bool b)
                boolValue = b;
            else if (value is bool?)
            {
                bool? nb = (bool?)value;
                boolValue = nb.HasValue && nb.Value;
            }

            if (IsReversed)
                boolValue = !boolValue;

            if (boolValue)
                return Visibility.Visible;
            else
                return UseHidden ? Visibility.Hidden : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                bool result = visibility == Visibility.Visible;
                return IsReversed ? !result : result;
            }

            return false;
        }

        /// <summary>
        /// Static instance for true -> Visible, false -> Collapsed
        /// </summary>
        public static readonly Boolean2VisibilityConverter TrueToVisible = new Boolean2VisibilityConverter { IsReversed = false };

        /// <summary>
        /// Static instance for true -> Collapsed, false -> Visible
        /// </summary>
        public static readonly Boolean2VisibilityConverter TrueToCollapsed = new Boolean2VisibilityConverter { IsReversed = true };

        /// <summary>
        /// Static instance for true -> Visible, false -> Hidden
        /// </summary>
        public static readonly Boolean2VisibilityConverter TrueToVisibleUseHidden = new Boolean2VisibilityConverter { IsReversed = false, UseHidden = true };

        /// <summary>
        /// Static instance for true -> Hidden, false -> Visible
        /// </summary>
        public static readonly Boolean2VisibilityConverter TrueToHiddenUseHidden = new Boolean2VisibilityConverter { IsReversed = true, UseHidden = true };
    }

    public class BoolToStatusBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isEnabled)
            {
                return new SolidColorBrush(isEnabled ? Colors.LimeGreen : Colors.Red);
            }

            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToStringConverter : IValueConverter
    {
        public static BoolToStringConverter Instance { get; } = new BoolToStringConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string paramString)
            {
                var parts = paramString.Split('|');
                if (parts.Length >= 2)
                    return boolValue ? parts[0] : parts[1];
            }

            return value?.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DoubleGreaterThanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            double threshold;
            double compareValue;

            if (!double.TryParse(parameter.ToString(), out threshold))
                return false;

            if (!double.TryParse(value.ToString(), out compareValue))
                return false;

            return compareValue > threshold;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DoubleLessThanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            double threshold;
            double compareValue;

            if (!double.TryParse(parameter.ToString(), out threshold))
                return false;

            if (!double.TryParse(value.ToString(), out compareValue))
                return false;

            return compareValue < threshold;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DoubleLessThanOrEqualConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            double threshold;
            double compareValue;

            if (!double.TryParse(parameter.ToString(), out threshold))
                return false;

            if (!double.TryParse(value.ToString(), out compareValue))
                return false;

            return compareValue <= threshold;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}