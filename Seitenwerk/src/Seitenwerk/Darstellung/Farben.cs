using System.Windows.Media;

namespace Seitenwerk.Darstellung;

/// <summary>Farbwerte als Text ("#RRGGBB" oder "#AARRGGBB") in WPF-Farben und eingefrorene Pinsel umwandeln.</summary>
public static class Farben
{
    private static readonly Dictionary<string, SolidColorBrush> PinselCache = new();
    private static readonly object Sperre = new();

    public static Color? Lesen(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static Color LesenOderSchwarz(string? hex) => Lesen(hex) ?? Colors.Black;

    public static SolidColorBrush? Pinsel(string? hex, double deckkraft = 1)
    {
        var farbe = Lesen(hex);
        if (farbe == null)
            return null;
        var schluessel = $"{hex}|{deckkraft:0.###}";
        lock (Sperre)
        {
            if (!PinselCache.TryGetValue(schluessel, out var pinsel))
            {
                pinsel = new SolidColorBrush(farbe.Value) { Opacity = Math.Clamp(deckkraft, 0, 1) };
                pinsel.Freeze();
                PinselCache[schluessel] = pinsel;
            }
            return pinsel;
        }
    }

    public static string Hex(Color farbe) => farbe.A == 255
        ? $"#{farbe.R:X2}{farbe.G:X2}{farbe.B:X2}"
        : $"#{farbe.A:X2}{farbe.R:X2}{farbe.G:X2}{farbe.B:X2}";

    /// <summary>Die Palette der Standardfarben im Menüband (Reihen: Grau, Hell, Mittel, Dunkel).</summary>
    public static readonly string[] Palette =
    {
        "#FFFFFF", "#F2F2F2", "#D9D9D9", "#A6A6A6", "#595959", "#262626", "#000000",
        "#FFC7CE", "#FF0000", "#C00000", "#FFE699", "#FFC000", "#ED7D31", "#843C0C",
        "#C6E0B4", "#92D050", "#00B050", "#548235", "#BDD7EE", "#00B0F0", "#0070C0",
        "#4472C4", "#1F3864", "#D9C3E5", "#7030A0", "#FF66CC", "#B4005A", "#FFFF00",
    };
}
