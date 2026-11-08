using System.Globalization;

namespace MapoMyslacz.Core.Model;

/// <summary>Kolor zapisywany w pliku jako „#RRGGBB” albo „#RRGGBBAA” (przezroczystość na końcu, jak w CSS).</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Rgba Black = new(0, 0, 0);
    public static readonly Rgba White = new(255, 255, 255);
    public static readonly Rgba Transparent = new(0, 0, 0, 0);

    public static bool TryParse(string? text, out Rgba color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length == 3) s = string.Concat(s.Select(ch => $"{ch}{ch}"));
        if (s.Length is not (6 or 8)) return false;
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = s.Length == 6
            ? new Rgba((byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : new Rgba((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static Rgba Parse(string? text, Rgba fallback) => TryParse(text, out var c) ? c : fallback;

    public override string ToString() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}{A:X2}";

    /// <summary>Względna luminancja (0 = czarny, 1 = biały) wg WCAG.</summary>
    public double Luminance
    {
        get
        {
            static double Lin(byte c)
            {
                double v = c / 255.0;
                return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Lin(R) + 0.7152 * Lin(G) + 0.0722 * Lin(B);
        }
    }

    /// <summary>Czy na tym kolorze lepiej czytać biały tekst.</summary>
    public bool PrefersLightText => A > 96 && Luminance < 0.32;

    public Rgba WithAlpha(byte a) => this with { A = a };

    /// <summary>Mieszanie z innym kolorem (t = 0 → ten, t = 1 → tamten).</summary>
    public Rgba Mix(Rgba other, double t)
    {
        byte M(byte a, byte b) => (byte)Math.Round(a + (b - a) * Math.Clamp(t, 0, 1));
        return new Rgba(M(R, other.R), M(G, other.G), M(B, other.B), M(A, other.A));
    }
}
