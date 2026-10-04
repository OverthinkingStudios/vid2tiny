using System.Windows.Media;

namespace Vid2Tiny.Infrastructure;

public static class TagPalette
{
    private static readonly double[] Hues = [210, 30, 120, 300, 180, 50, 270, 0, 150, 330, 90, 240];

    public static (Brush Tint, Brush Accent) Get(int index)
    {
        var hue = (Hues[index % Hues.Length] + 15 * (index / Hues.Length)) % 360;
        return (Frozen(FromHsl(hue, 0.60, 0.93)), Frozen(FromHsl(hue, 0.55, 0.38)));
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = lightness - chroma / 2;
        var (r, g, b) = (int)(hue / 60) switch
        {
            0 => (chroma, x, 0d),
            1 => (x, chroma, 0d),
            2 => (0d, chroma, x),
            3 => (0d, x, chroma),
            4 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return Color.FromRgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static byte ToByte(double channel) => (byte)Math.Round(channel * 255);
}
