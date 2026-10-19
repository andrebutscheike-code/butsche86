using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using Seitenwerk.Core.Modell;
using Seitenwerk.Darstellung;

namespace Seitenwerk.Werkzeuge;

/// <summary>Erzeugt die Symbole des Menübands als Vektorgrafiken (Schriftsymbole von Windows oder einfache Skizzen).</summary>
public static class Symbole
{
    public const string SymbolSchrift = "Segoe MDL2 Assets";
    private static readonly Dictionary<string, ImageSource> Cache = new();

    private static Brush Pinsel(string farbe) => Farben.Pinsel(farbe) ?? Brushes.Black;

    /// <summary>Ein Zeichen der Symbolschrift, mittig in einem Quadrat der Kantenlänge <paramref name="groesse"/>.</summary>
    public static ImageSource Glyph(string zeichen, double groesse = 32, string farbe = "#2B579A")
    {
        var schluessel = $"g|{zeichen}|{groesse}|{farbe}";
        if (Cache.TryGetValue(schluessel, out var vorhanden))
            return vorhanden;

        var pinsel = Pinsel(farbe);
        var text = new FormattedText(zeichen, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(SymbolSchrift), groesse * 0.8, pinsel, 1.0);
        var geometrie = text.BuildGeometry(new Point(0, 0));
        var gruppe = new DrawingGroup();
        gruppe.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, groesse, groesse))));
        if (!geometrie.IsEmpty())
        {
            var rand = geometrie.Bounds;
            var verschiebung = new TranslateTransform(
                (groesse - rand.Width) / 2 - rand.X,
                (groesse - rand.Height) / 2 - rand.Y);
            geometrie.Transform = verschiebung;
            gruppe.Children.Add(new GeometryDrawing(pinsel, null, geometrie));
        }
        gruppe.Freeze();
        var bild = new DrawingImage(gruppe);
        bild.Freeze();
        Cache[schluessel] = bild;
        return bild;
    }

    /// <summary>Eine Linienskizze: Pfaddaten in einem 32×32-Raster.</summary>
    public static ImageSource Skizze(string pfad, string farbe = "#2B579A", bool gefuellt = false, double staerke = 2)
    {
        var schluessel = $"s|{pfad}|{farbe}|{gefuellt}|{staerke}";
        if (Cache.TryGetValue(schluessel, out var vorhanden))
            return vorhanden;

        var pinsel = Pinsel(farbe);
        var stift = new Pen(pinsel, staerke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var gruppe = new DrawingGroup();
        gruppe.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
        gruppe.Children.Add(new GeometryDrawing(gefuellt ? pinsel : null, stift, Geometry.Parse(pfad)));
        gruppe.Freeze();
        var bild = new DrawingImage(gruppe);
        bild.Freeze();
        Cache[schluessel] = bild;
        return bild;
    }

    public static ImageSource Form(FormArt form, string farbe = "#2B579A")
    {
        var schluessel = $"f|{form}|{farbe}";
        if (Cache.TryGetValue(schluessel, out var vorhanden))
            return vorhanden;

        Geometry geometrie;
        var feld = new Rect(4, 6, 24, 20);
        switch (form)
        {
            case FormArt.Rechteck:
                geometrie = new RectangleGeometry(feld);
                break;
            case FormArt.AbgerundetesRechteck:
                geometrie = new RectangleGeometry(feld, 6, 6);
                break;
            case FormArt.Ellipse:
                geometrie = new EllipseGeometry(feld);
                break;
            default:
                var punkte = Geometrie.Polygon(form);
                var quadrat = new Rect(4, 4, 24, 24);
                geometrie = punkte == null ? new RectangleGeometry(feld) : SeitenRenderer.PolygonGeometrie(punkte, quadrat);
                break;
        }

        var pinsel = Pinsel(farbe);
        var gruppe = new DrawingGroup();
        gruppe.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
        gruppe.Children.Add(new GeometryDrawing(Farben.Pinsel("#DCE6F5"), new Pen(pinsel, 1.8) { LineJoin = PenLineJoin.Round }, geometrie));
        gruppe.Freeze();
        var bild = new DrawingImage(gruppe);
        bild.Freeze();
        Cache[schluessel] = bild;
        return bild;
    }

    public static ImageSource FarbFeld(string farbe)
    {
        var schluessel = $"c|{farbe}";
        if (Cache.TryGetValue(schluessel, out var vorhanden))
            return vorhanden;
        var gruppe = new DrawingGroup();
        gruppe.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
        gruppe.Children.Add(new GeometryDrawing(Farben.Pinsel(farbe) ?? Brushes.Transparent, new Pen(Brushes.Gray, 1), new RectangleGeometry(new Rect(4, 4, 24, 24))));
        gruppe.Freeze();
        var bild = new DrawingImage(gruppe);
        bild.Freeze();
        Cache[schluessel] = bild;
        return bild;
    }

    // Häufig verwendete Skizzen
    public const string Textfeld = "M4,6 H28 V26 H4 Z M9,12 H23 M9,17 H23 M9,22 H17";
    public const string Bild = "M4,6 H28 V26 H4 Z M4,22 L11,15 L16,20 L21,13 L28,22 M24,11 A2,2 0 1 1 20,11 A2,2 0 1 1 24,11";
    public const string Linie = "M5,27 L27,5";
    public const string Pfeil = "M5,27 L27,5 M27,5 H17 M27,5 V15";
    public const string Seite = "M8,3 H20 L26,9 V29 H8 Z M20,3 V9 H26";
    public const string SeiteNeu = "M8,3 H20 L26,9 V29 H8 Z M20,3 V9 H26 M17,15 V24 M12.5,19.5 H21.5";
    public const string SeiteKopie = "M6,6 H16 L21,11 V25 H6 Z M16,6 V11 H21 M11,2 H24 L28,6 V21";
    public const string SeiteWeg = "M8,3 H20 L26,9 V29 H8 Z M20,3 V9 H26 M13,17 L21,25 M21,17 L13,25";
    public const string Links = "M4,7 H26 M4,13 H18 M4,19 H26 M4,25 H14";
    public const string Zentriert = "M5,7 H27 M9,13 H23 M5,19 H27 M11,25 H21";
    public const string Rechts = "M6,7 H28 M14,13 H28 M6,19 H28 M18,25 H28";
    public const string Blocksatz = "M4,7 H28 M4,13 H28 M4,19 H28 M4,25 H28";
    public const string ObenAusrichten = "M4,5 H28 M10,5 V25 M22,5 V17";
    public const string MitteAusrichten = "M4,16 H28 M10,6 V26 M22,10 V22";
    public const string UntenAusrichten = "M4,27 H28 M10,7 V27 M22,15 V27";
    public const string LinksAusrichten = "M5,4 V28 M5,10 H25 M5,22 H17";
    public const string ZentrumAusrichten = "M16,4 V28 M6,10 H26 M10,22 H22";
    public const string RechtsAusrichten = "M27,4 V28 M27,10 H7 M27,22 H15";
    public const string Gruppe = "M4,4 H18 V18 H4 Z M14,14 H28 V28 H14 Z";
    public const string GruppeAufheben = "M4,4 H15 V15 H4 Z M17,17 H28 V28 H17 Z";
    public const string Vordergrund = "M12,12 H28 V28 H12 Z M4,4 H20 V12 M4,4 V20 H12";
    public const string Hintergrund = "M4,4 H20 V20 H4 Z M20,12 H28 V28 H12 V20";
    public const string Drehen = "M25,16 A9,9 0 1 1 16,7 M16,7 L11,3 M16,7 L11,11";
    public const string Raster = "M6,6 H26 V26 H6 Z M6,12.7 H26 M6,19.3 H26 M12.7,6 V26 M19.3,6 V26";
    public const string Lineal = "M3,10 H29 V22 H3 Z M8,10 V16 M13,10 V14 M18,10 V16 M23,10 V14";
    public const string Raender = "M5,5 H27 V27 H5 Z M9,9 H23 V23 H9 Z";
    public const string Navigation = "M4,5 H12 V13 H4 Z M4,19 H12 V27 H4 Z M17,6 H28 M17,12 H26 M17,20 H28 M17,26 H26";
    public const string Fangen = "M16,4 V28 M4,16 H28 M16,16 m-5,0 a5,5 0 1 0 10,0 a5,5 0 1 0 -10,0";
    public const string Hoch = "M9,4 H23 V28 H9 Z";
    public const string Quer = "M4,9 H28 V23 H4 Z";
    public const string GanzeSeite = "M10,4 H22 V28 H10 Z M4,10 V4 H8 M28,10 V4 H24 M4,22 V28 H8 M28,22 V28 H24";
    public const string Seitenbreite = "M6,6 H26 V26 H6 Z M2,16 H6 M26,16 H30 M4,13 L2,16 L4,19 M28,13 L30,16 L28,19";
    public const string Verteilen = "M4,6 V26 M28,6 V26 M14,9 H18 V23 H14 Z M9,13 L5,16 L9,19 M23,13 L27,16 L23,19";
    public const string Sperren = "M9,14 H23 V27 H9 Z M12,14 V10 A4,4 0 0 1 20,10 V14";
    public const string Rechtschreibung = "M5,22 L9,10 L13,22 M6.5,18 H11.5 M16,18 L20,23 L28,11";
    public const string Duplizieren = "M4,4 H18 V18 H4 Z M12,12 H26 V26 H12 Z";
}

/// <summary>Verwendung in XAML: <c>LargeImageSource="{local:Symbol Glyph=E74E}"</c>, <c>{local:Symbol Skizze=Textfeld}</c> oder <c>{local:Symbol Form=Ellipse}</c>.</summary>
[MarkupExtensionReturnType(typeof(ImageSource))]
public sealed class SymbolExtension : MarkupExtension
{
    public string? Glyph { get; set; }
    public string? Skizze { get; set; }
    public FormArt? Form { get; set; }
    public string? Farbe { get; set; }
    public bool Gefuellt { get; set; }

    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        // Im XAML-Designer oder bei fehlender Schrift darf nichts abstürzen: dann bleibt das Symbol leer.
        try
        {
            if (Form != null)
                return Symbole.Form(Form.Value, Farbe ?? "#2B579A");
            if (!string.IsNullOrEmpty(Skizze))
            {
                var daten = typeof(Symbole).GetField(Skizze)?.GetValue(null) as string ?? Skizze;
                return Symbole.Skizze(daten, Farbe ?? "#2B579A", Gefuellt);
            }
            if (!string.IsNullOrEmpty(Glyph))
            {
                var zeichen = char.ConvertFromUtf32(int.Parse(Glyph, NumberStyles.HexNumber));
                return Symbole.Glyph(zeichen, 32, Farbe ?? "#2B579A");
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
        }
        return null;
    }
}
