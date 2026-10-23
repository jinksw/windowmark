using System;
using System.Windows.Media;

namespace WindowMark.UI;

internal static class Palette
{
    public static readonly Color LayoutAccent = Color.FromRgb(0x4C, 0x9A, 0xFF);

    public static readonly string[] Colors =
    {
        "#FFD166",
        "#8ECAE6",
        "#A7E8A4",
        "#F4A6C0",
        "#C9B6F5",
        "#FFB38A",
        "#FF9B9B",
        "#D9D9D9"
    };

    public static string DefaultFor(string identity)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in identity)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return Colors[(int)(hash % (uint)Colors.Length)];
        }
    }

    public static bool TryParse(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        try
        {
            if (ColorConverter.ConvertFromString(hex.Trim()) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch
        {
        }
        return false;
    }

    public static Color Parse(string hex)
        => TryParse(hex, out var color) ? color : (Color)ColorConverter.ConvertFromString(Colors[0])!;

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Brush TextBrushFor(Color background)
    {
        double luminance =
            0.2126 * Linear(background.R) +
            0.7152 * Linear(background.G) +
            0.0722 * Linear(background.B);

        return luminance > 0.55
            ? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A))
            : Brushes.White;

        static double Linear(byte value)
        {
            double channel = value / 255.0;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
    }
}
