// Extracts dominant colors from album art for adaptive theme gradients.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

namespace TaskbarMiniPlayer
{
    public class ColorExtractor
    {
        private BitmapSource? _lastAlbumArtForColors;
        private (Color, Color) _cachedGradientColors = (Color.FromRgb(30, 30, 34), Color.FromRgb(15, 15, 17));

        public (Color color1, Color color2) GetCachedGradientColors(BitmapSource bmp)
        {
            if (bmp == _lastAlbumArtForColors)
                return _cachedGradientColors;

            _lastAlbumArtForColors = bmp;
            _cachedGradientColors = ExtractGradientColors(bmp);
            return _cachedGradientColors;
        }

        public void ResetCache()
        {
            _lastAlbumArtForColors = null;
            _cachedGradientColors = (Color.FromRgb(30, 30, 34), Color.FromRgb(15, 15, 17));
        }

        public static (Color, Color) ExtractGradientColors(BitmapSource bmp)
        {
            try
            {
                var formatted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                int w = formatted.PixelWidth;
                int h = formatted.PixelHeight;
                int stride = w * 4;
                var pixels = new byte[stride * h];
                formatted.CopyPixels(pixels, stride, 0);

                var buckets = new Dictionary<int, (long r, long g, long b, int count)>();
                int stepX = Math.Max(1, w / 30);
                int stepY = Math.Max(1, h / 30);
                
                long totalR = 0, totalG = 0, totalB = 0;
                int sampledCount = 0;

                for (int y = 0; y < h; y += stepY)
                {
                    int rowOff = y * stride;
                    for (int x = 0; x < w; x += stepX)
                    {
                        int i = rowOff + x * 4;
                        if (i + 2 >= pixels.Length) continue;

                        byte b = pixels[i];
                        byte g = pixels[i + 1];
                        byte r = pixels[i + 2];

                        totalR += r;
                        totalG += g;
                        totalB += b;
                        sampledCount++;

                        int lum = (r * 299 + g * 587 + b * 114) / 1000;
                        if (lum < 20 || lum > 240) continue;

                        int key = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                        if (buckets.TryGetValue(key, out var v))
                        {
                            buckets[key] = (v.r + r, v.g + g, v.b + b, v.count + 1);
                        }
                        else
                        {
                            buckets[key] = (r, g, b, 1);
                        }
                    }
                }

                if (buckets.Count == 0)
                {
                    if (sampledCount > 0)
                    {
                        var avgColor = Color.FromRgb((byte)(totalR / sampledCount), (byte)(totalG / sampledCount), (byte)(totalB / sampledCount));
                        var darkerColor = Color.FromRgb((byte)(avgColor.R * 0.5), (byte)(avgColor.G * 0.5), (byte)(avgColor.B * 0.5));
                        return (avgColor, darkerColor);
                    }
                    return (Color.FromRgb(30, 30, 34), Color.FromRgb(15, 15, 17));
                }

                var sorted = buckets.Values.OrderByDescending(v => v.count).ToList();
                var top1 = sorted[0];
                Color color1 = Color.FromRgb(
                    (byte)(top1.r / top1.count),
                    (byte)(top1.g / top1.count),
                    (byte)(top1.b / top1.count));

                Color color2 = color1;
                bool foundSecond = false;

                for (int i = 1; i < sorted.Count; i++)
                {
                    var top2 = sorted[i];
                    Color c2 = Color.FromRgb(
                        (byte)(top2.r / top2.count),
                        (byte)(top2.g / top2.count),
                        (byte)(top2.b / top2.count));

                    double distance = Math.Sqrt(
                        Math.Pow(color1.R - c2.R, 2) + 
                        Math.Pow(color1.G - c2.G, 2) + 
                        Math.Pow(color1.B - c2.B, 2)
                    );

                    if (distance > 50)
                    {
                        color2 = c2;
                        foundSecond = true;
                        break;
                    }
                }

                if (!foundSecond)
                {
                    color2 = Color.FromRgb(
                        (byte)(color1.R * 0.5),
                        (byte)(color1.G * 0.5),
                        (byte)(color1.B * 0.5));
                }

                return (color1, color2);
            }
            catch
            {
                return (Color.FromRgb(30, 30, 34), Color.FromRgb(15, 15, 17));
            }
        }

        public static Color Blend(Color baseColor, Color tintColor, double ratio)
        {
            return Color.FromRgb(
                (byte)(baseColor.R * (1 - ratio) + tintColor.R * ratio),
                (byte)(baseColor.G * (1 - ratio) + tintColor.G * ratio),
                (byte)(baseColor.B * (1 - ratio) + tintColor.B * ratio)
            );
        }

        public static Color BrightenColorIfDark(Color color, double minLuminance = 0.45)
        {
            double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
            if (luminance < minLuminance)
            {
                double scale = minLuminance / Math.Max(luminance, 0.01);
                scale = Math.Min(scale, 3.0);

                byte r = (byte)Math.Min(255, color.R * scale);
                byte g = (byte)Math.Min(255, color.G * scale);
                byte b = (byte)Math.Min(255, color.B * scale);

                double finalLuminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
                if (finalLuminance < minLuminance)
                {
                    double blendFactor = minLuminance - finalLuminance;
                    r = (byte)Math.Min(255, r + (255 - r) * blendFactor);
                    g = (byte)Math.Min(255, g + (255 - g) * blendFactor);
                    b = (byte)Math.Min(255, b + (255 - b) * blendFactor);
                }

                return Color.FromRgb(r, g, b);
            }
            return color;
        }
    }
}
