using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Seitenwerk.Core.Modell;

namespace Seitenwerk.Core.Import;

/// <summary>
/// Liest OpenDocument-Zeichnungen (.odg) und baut daraus ein <see cref="Dokument"/>.
/// LibreOffice wandelt Publisher-Dateien in dieses Format um; der Importer ist auf dessen Ausgabe ausgelegt.
/// </summary>
public static class OdgImporter
{
    public static ImportErgebnis Importieren(string pfad, bool ausPublisher = false)
    {
        using var datei = File.OpenRead(pfad);
        return Importieren(datei, Path.GetFileNameWithoutExtension(pfad), ausPublisher);
    }

    /// <param name="ausPublisher">Die Zeichnung stammt aus einer Publisher-Datei: Textrahmen behalten ihre ursprüngliche Größe,
    /// auch wenn LibreOffice sie wegen überlangem Text vergrößert hat (Publisher blendet Überlauf aus).</param>
    public static ImportErgebnis Importieren(Stream stream, string? titel = null, bool ausPublisher = false)
    {
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return new OdgLeser(zip, ausPublisher).Lesen(titel);
        }
        catch (InvalidDataException ex)
        {
            throw new ImportFehler("Die Datei ist keine gültige OpenDocument-Zeichnung.", ex);
        }
    }
}

internal readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
{
    public static readonly Matrix Einheit = new(1, 0, 0, 1, 0, 0);

    public (double X, double Y) Anwenden(double x, double y) => (A * x + C * y + E, B * x + D * y + F);

    /// <summary>Erst <paramref name="zuerst"/>, danach diese Matrix.</summary>
    public Matrix Mal(Matrix zuerst) => new(
        A * zuerst.A + C * zuerst.B,
        B * zuerst.A + D * zuerst.B,
        A * zuerst.C + C * zuerst.D,
        B * zuerst.C + D * zuerst.D,
        A * zuerst.E + C * zuerst.F + E,
        B * zuerst.E + D * zuerst.F + F);

    public double DrehwinkelGrad => Math.Atan2(B, A) * 180 / Math.PI;
    public double SkalierungX => Math.Sqrt(A * A + B * B);
    public double SkalierungY => Math.Sqrt(C * C + D * D);
}

internal sealed class OdgStil
{
    public string Familie = "";
    public string? Eltern;
    public readonly Dictionary<string, string> Grafik = new();
    public readonly Dictionary<string, string> Text = new();
    public readonly Dictionary<string, string> Absatz = new();
    public readonly Dictionary<string, string> Seite = new();
    public readonly Dictionary<string, string> Tabelle = new();
}

internal sealed class OdgLeser
{
    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    private static readonly XNamespace Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    private static readonly XNamespace Svg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";
    private static readonly XNamespace Xlink = "http://www.w3.org/1999/xlink";
    private static readonly XNamespace Presentation = "urn:oasis:names:tc:opendocument:xmlns:presentation:1.0";
    private static readonly XNamespace Tabellen = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";

    private static readonly HashSet<string> ErlaubteStilNamensraeume = new()
    {
        Style.NamespaceName, Fo.NamespaceName, Svg.NamespaceName, Draw.NamespaceName, Text.NamespaceName,
    };

    private readonly ZipArchive _zip;
    private readonly Dictionary<string, OdgStil> _stile = new();
    private readonly Dictionary<string, OdgStil> _standard = new();
    private readonly Dictionary<string, string> _schriften = new();
    private readonly Dictionary<string, (string Start, string Ende)> _verlaeufe = new();
    private readonly Dictionary<string, string> _fuellBilder = new();
    private readonly Dictionary<string, XElement> _masterSeiten = new();
    private readonly Dictionary<string, XElement> _seitenLayouts = new();
    private readonly Dictionary<string, Medium> _medien = new();
    private readonly List<string> _hinweise = new();
    private readonly bool _ausPublisher;
    private Dokument _dokument = new();

    public OdgLeser(ZipArchive zip, bool ausPublisher)
    {
        _zip = zip;
        _ausPublisher = ausPublisher;
    }

    public ImportErgebnis Lesen(string? titel)
    {
        var inhalt = LadeXml("content.xml") ?? throw new ImportFehler("Die Datei enthält keinen Inhalt (content.xml fehlt).");
        var stile = LadeXml("styles.xml");

        foreach (var wurzel in new[] { stile, inhalt })
        {
            if (wurzel?.Root == null)
                continue;
            LeseSchriften(wurzel.Root);
            LeseStile(wurzel.Root);
        }

        _dokument = new Dokument { Titel = titel, Seiten = new List<Seite>() };

        var zeichnung = inhalt.Root!.Element(Office + "body")?.Element(Office + "drawing")
            ?? throw new ImportFehler("Die Datei enthält keine Zeichnung (Seiten).");

        var seiten = zeichnung.Elements(Draw + "page").ToList();
        if (seiten.Count == 0)
            throw new ImportFehler("Die Datei enthält keine Seiten.");

        // Seitengröße und Ränder aus der Musterseite der ersten Seite.
        BestimmeSeitengroesse(seiten[0]);

        foreach (var seitenElement in seiten)
            _dokument.Seiten.Add(LeseSeite(seitenElement));

        return new ImportErgebnis(_dokument, _hinweise);
    }

    // ---------------------------------------------------------------- Laden

    private XDocument? LadeXml(string name)
    {
        var eintrag = _zip.GetEntry(name);
        if (eintrag == null)
            return null;
        using var s = eintrag.Open();
        return XDocument.Load(s);
    }

    private void Hinweis(string text)
    {
        if (!_hinweise.Contains(text))
            _hinweise.Add(text);
    }

    private void LeseSchriften(XElement wurzel)
    {
        foreach (var f in wurzel.Descendants(Style + "font-face"))
        {
            var name = (string?)f.Attribute(Style + "name");
            var familie = (string?)f.Attribute(Svg + "font-family");
            if (name != null && familie != null)
                _schriften[name] = familie.Trim('\'', '"');
        }
    }

    private void LeseStile(XElement wurzel)
    {
        foreach (var g in wurzel.Descendants(Draw + "gradient"))
        {
            var name = (string?)g.Attribute(Draw + "name");
            var start = (string?)g.Attribute(Draw + "start-color");
            var ende = (string?)g.Attribute(Draw + "end-color");
            if (name != null && start != null && ende != null)
                _verlaeufe[name] = (start, ende);
        }

        foreach (var f in wurzel.Descendants(Draw + "fill-image"))
        {
            var name = (string?)f.Attribute(Draw + "name");
            var href = (string?)f.Attribute(Xlink + "href");
            if (name != null && href != null)
                _fuellBilder[name] = href;
        }

        foreach (var m in wurzel.Descendants(Style + "master-page"))
        {
            var name = (string?)m.Attribute(Style + "name");
            if (name != null)
                _masterSeiten[name] = m;
        }

        foreach (var l in wurzel.Descendants(Style + "page-layout"))
        {
            var name = (string?)l.Attribute(Style + "name");
            if (name != null)
                _seitenLayouts[name] = l;
        }

        foreach (var d in wurzel.Descendants(Style + "default-style"))
        {
            var familie = (string?)d.Attribute(Style + "family");
            if (familie != null)
                _standard[familie] = LeseStil(d, familie);
        }

        foreach (var s in wurzel.Descendants(Style + "style"))
        {
            var name = (string?)s.Attribute(Style + "name");
            var familie = (string?)s.Attribute(Style + "family");
            if (name == null || familie == null)
                continue;
            var stil = LeseStil(s, familie);
            stil.Eltern = (string?)s.Attribute(Style + "parent-style-name");
            _stile[Schluessel(familie, name)] = stil;
        }
    }

    private static string Schluessel(string familie, string name) => familie + ":" + name;

    private static OdgStil LeseStil(XElement element, string familie)
    {
        var stil = new OdgStil { Familie = familie };
        foreach (var kind in element.Elements())
        {
            Dictionary<string, string>? ziel = null;
            if (kind.Name == Style + "graphic-properties")
                ziel = stil.Grafik;
            else if (kind.Name == Style + "text-properties")
                ziel = stil.Text;
            else if (kind.Name == Style + "paragraph-properties")
                ziel = stil.Absatz;
            else if (kind.Name == Style + "drawing-page-properties")
                ziel = stil.Seite;
            else if (kind.Name == Style + "table-column-properties" || kind.Name == Style + "table-row-properties"
                     || kind.Name == Style + "table-cell-properties" || (kind.Name.LocalName == "graphic-properties" && familie == "table-cell"))
                ziel = stil.Tabelle;
            if (ziel == null)
                continue;
            foreach (var a in kind.Attributes())
            {
                if (a.IsNamespaceDeclaration || !ErlaubteStilNamensraeume.Contains(a.Name.NamespaceName))
                    continue;
                ziel[a.Name.LocalName] = a.Value;
            }
        }
        return stil;
    }

    /// <summary>Fasst die Eigenschaften eines Stils samt Elternstilen zusammen (Kind überschreibt Eltern).</summary>
    private Dictionary<string, string> Aufloesen(string familie, string? name, Func<OdgStil, Dictionary<string, string>> gruppe, bool mitStandard)
    {
        var ergebnis = new Dictionary<string, string>();
        if (mitStandard && _standard.TryGetValue(familie, out var std))
            Mischen(ergebnis, gruppe(std));

        var kette = new List<OdgStil>();
        var aktuell = name;
        while (aktuell != null && kette.Count < 20 && _stile.TryGetValue(Schluessel(familie, aktuell), out var stil))
        {
            kette.Add(stil);
            aktuell = stil.Eltern;
        }
        kette.Reverse();
        foreach (var stil in kette)
            Mischen(ergebnis, gruppe(stil));
        return ergebnis;
    }

    private static void Mischen(Dictionary<string, string> ziel, Dictionary<string, string> quelle)
    {
        foreach (var (schluessel, wert) in quelle)
        {
            if (schluessel == "font-size" && wert.EndsWith('%'))
            {
                var basis = ziel.TryGetValue("font-size", out var alt) ? Punkt(alt) ?? 12 : 12;
                if (double.TryParse(wert.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var prozent))
                {
                    ziel[schluessel] = (basis * prozent / 100).ToString("0.##", CultureInfo.InvariantCulture) + "pt";
                    continue;
                }
            }
            ziel[schluessel] = wert;
        }
    }

    // ---------------------------------------------------------------- Einheiten

    private static readonly Regex LaengeRegex = new(@"^\s*(-?\d*\.?\d+(?:[eE][-+]?\d+)?)\s*(cm|mm|in|pt|pc|px|m)?\s*$", RegexOptions.Compiled);

    /// <summary>Länge in Millimetern.</summary>
    internal static double? Laenge(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var m = LaengeRegex.Match(text);
        if (!m.Success)
            return null;
        var zahl = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return m.Groups[2].Value switch
        {
            "cm" => zahl * 10,
            "mm" => zahl,
            "in" => zahl * 25.4,
            "pt" => zahl * 25.4 / 72,
            "pc" => zahl * 25.4 / 6,
            "px" => zahl * 25.4 / 96,
            "m" => zahl * 1000,
            _ => zahl * 10, // ohne Einheit: wie in ODF üblich Zentimeter
        };
    }

    /// <summary>Länge in Punkt.</summary>
    private static double? Punkt(string? text) => Laenge(text) is { } mm ? mm * 72 / 25.4 : null;

    private double LaengeAttr(XElement e, XName name, double standard = 0) => Laenge((string?)e.Attribute(name)) ?? standard;

    // ---------------------------------------------------------------- Seiten

    private void BestimmeSeitengroesse(XElement ersteSeite)
    {
        var masterName = (string?)ersteSeite.Attribute(Draw + "master-page-name");
        XElement? layout = null;
        if (masterName != null && _masterSeiten.TryGetValue(masterName, out var master))
        {
            var layoutName = (string?)master.Attribute(Style + "page-layout-name");
            if (layoutName != null)
                _seitenLayouts.TryGetValue(layoutName, out layout);
        }
        layout ??= _seitenLayouts.Values.FirstOrDefault();
        var eigenschaften = layout?.Element(Style + "page-layout-properties");

        var breite = Laenge((string?)eigenschaften?.Attribute(Fo + "page-width")) ?? 210;
        var hoehe = Laenge((string?)eigenschaften?.Attribute(Fo + "page-height")) ?? 297;
        if (breite < 10 || hoehe < 10)
        {
            breite = 210;
            hoehe = 297;
        }
        _dokument.SeitenBreite = Math.Round(breite, 2);
        _dokument.SeitenHoehe = Math.Round(hoehe, 2);

        double Rand(XName n)
        {
            var wert = Laenge((string?)eigenschaften?.Attribute(n));
            return wert is > 0 && wert < Math.Min(breite, hoehe) / 4 ? Math.Round(wert.Value, 2) : 10;
        }

        _dokument.Raender = new Seitenraender
        {
            Oben = Rand(Fo + "margin-top"),
            Unten = Rand(Fo + "margin-bottom"),
            Links = Rand(Fo + "margin-left"),
            Rechts = Rand(Fo + "margin-right"),
        };
    }

    private Seite LeseSeite(XElement element)
    {
        var seite = new Seite();

        var seitenStil = (string?)element.Attribute(Draw + "style-name");
        var eigenschaften = Aufloesen("drawing-page", seitenStil, s => s.Seite, false);
        if (eigenschaften.TryGetValue("fill", out var fuellart) && fuellart == "solid" && Farbe(eigenschaften.GetValueOrDefault("fill-color")) is { } hintergrund)
            seite.Hintergrund = hintergrund;

        // Inhalte der Musterseite liegen hinter den Seitenobjekten.
        var masterName = (string?)element.Attribute(Draw + "master-page-name");
        if (masterName != null && _masterSeiten.TryGetValue(masterName, out var master))
        {
            foreach (var kind in master.Elements())
                Verarbeite(kind, Matrix.Einheit, seite.Objekte, istMuster: true);
        }

        foreach (var kind in element.Elements())
            Verarbeite(kind, Matrix.Einheit, seite.Objekte, istMuster: false);

        return seite;
    }

    // ---------------------------------------------------------------- Formen

    private void Verarbeite(XElement e, Matrix eltern, List<Objekt> ziel, bool istMuster)
    {
        if (e.Name.Namespace == Presentation)
            return;
        if (e.Name.Namespace != Draw)
            return;
        if ((string?)e.Attribute(Presentation + "class") != null)
            return; // Platzhalter

        switch (e.Name.LocalName)
        {
            case "frame":
                Rahmen(e, eltern, ziel);
                break;
            case "rect":
            case "ellipse":
            case "circle":
            case "custom-shape":
                TextForm(e, eltern, ziel);
                break;
            case "line":
            case "connector":
                Linie(e, eltern, ziel);
                break;
            case "polygon":
            case "polyline":
            case "path":
                Pfad(e, eltern, ziel);
                break;
            case "g":
                Gruppe(e, eltern, ziel, istMuster);
                break;
            case "a":
                foreach (var kind in e.Elements())
                    Verarbeite(kind, eltern, ziel, istMuster);
                break;
            case "page-thumbnail":
            case "layer-set":
            case "layer":
                break;
            default:
                Hinweis($"Element „{e.Name.LocalName}“ wird nicht unterstützt und wurde ausgelassen.");
                break;
        }
    }

    private Matrix Transformation(XElement e, Matrix eltern)
    {
        var m = Matrix.Einheit;
        var text = (string?)e.Attribute(Draw + "transform");
        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (Match op in Regex.Matches(text, @"(\w+)\s*\(([^)]*)\)"))
            {
                var name = op.Groups[1].Value;
                var args = Regex.Split(op.Groups[2].Value.Trim(), @"[\s,]+").Where(a => a.Length > 0).ToArray();
                Matrix schritt;
                switch (name)
                {
                    case "translate" when args.Length >= 1:
                        schritt = new Matrix(1, 0, 0, 1, Laenge(args[0]) ?? 0, args.Length > 1 ? Laenge(args[1]) ?? 0 : 0);
                        break;
                    case "rotate" when args.Length >= 1:
                    {
                        // ODF: positive Winkel drehen entgegen dem Uhrzeigersinn.
                        var inGrad = args[0].EndsWith("deg", StringComparison.Ordinal);
                        var a = double.Parse(args[0].Replace("deg", "").Replace("rad", ""), CultureInfo.InvariantCulture);
                        if (inGrad)
                            a = a * Math.PI / 180;
                        schritt = new Matrix(Math.Cos(a), -Math.Sin(a), Math.Sin(a), Math.Cos(a), 0, 0);
                        break;
                    }
                    case "scale" when args.Length >= 1:
                    {
                        var sx = double.Parse(args[0], CultureInfo.InvariantCulture);
                        var sy = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : sx;
                        schritt = new Matrix(sx, 0, 0, sy, 0, 0);
                        break;
                    }
                    default:
                        Hinweis($"Transformation „{name}“ wird nicht unterstützt.");
                        continue;
                }
                m = schritt.Mal(m); // Operationen wirken von links nach rechts
            }
        }
        return eltern.Mal(m);
    }

    private static string? Farbe(string? farbe)
    {
        if (string.IsNullOrWhiteSpace(farbe) || farbe == "transparent")
            return null;
        farbe = farbe.Trim();
        if (farbe.Length == 7 && farbe[0] == '#')
            return farbe.ToUpperInvariant();
        return null;
    }

    private static string Mischfarbe(string a, string b)
    {
        static int Teil(string f, int i) => Convert.ToInt32(f.Substring(1 + i * 2, 2), 16);
        if (Farbe(a) == null || Farbe(b) == null)
            return a;
        return "#" + string.Concat(Enumerable.Range(0, 3).Select(i => ((Teil(a, i) + Teil(b, i)) / 2).ToString("X2")));
    }

    /// <summary>Überträgt Füllung, Rahmen und Deckkraft aus den Grafikeigenschaften.</summary>
    private void FlaecheAnwenden(FlaechenObjekt objekt, Dictionary<string, string> g, bool fuellungErlaubt = true)
    {
        objekt.Fuellung = null;
        if (fuellungErlaubt && g.TryGetValue("fill", out var fuellart))
        {
            switch (fuellart)
            {
                case "solid":
                    objekt.Fuellung = Farbe(g.GetValueOrDefault("fill-color"));
                    break;
                case "gradient":
                    if (g.TryGetValue("fill-gradient-name", out var gname) && _verlaeufe.TryGetValue(gname, out var v))
                        objekt.Fuellung = Farbe(Mischfarbe(v.Start, v.Ende));
                    else
                        objekt.Fuellung = Farbe(g.GetValueOrDefault("fill-color"));
                    if (objekt.Fuellung != null)
                        Hinweis("Farbverläufe wurden durch eine einfarbige Füllung ersetzt.");
                    break;
                case "bitmap":
                    if (g.TryGetValue("fill-image-name", out var bildName) && _fuellBilder.TryGetValue(bildName, out var bildHref)
                        && LadeMedium(bildHref, null) is { } fuellMedium)
                        objekt.FuellungBildId = fuellMedium.Id;
                    break;
                case "hatch":
                    Hinweis("Schraffurfüllungen werden nicht unterstützt und wurden entfernt.");
                    break;
            }
        }

        if (g.TryGetValue("opacity", out var deckkraft) && deckkraft.EndsWith('%')
            && double.TryParse(deckkraft.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var prozent))
            objekt.Deckkraft = Math.Clamp(prozent / 100, 0, 1);

        var strich = g.GetValueOrDefault("stroke");
        if (strich is "solid" or "dash")
        {
            objekt.Rahmenfarbe = Farbe(g.GetValueOrDefault("stroke-color")) ?? "#000000";
            objekt.Rahmenbreite = Math.Max(0.25, Math.Round(Punkt(g.GetValueOrDefault("stroke-width")) ?? 0, 2));
            objekt.Rahmenart = strich == "dash" ? StrichartAus(g.GetValueOrDefault("stroke-dash")) : Strichart.Durchgezogen;
        }
        else
        {
            objekt.Rahmenfarbe = null;
        }
    }

    private static Strichart StrichartAus(string? dashName)
    {
        if (dashName == null)
            return Strichart.Gestrichelt;
        var n = dashName.ToLowerInvariant();
        return n.Contains("dot") && !n.Contains("dash") ? Strichart.Gepunktet : Strichart.Gestrichelt;
    }

    private (double X, double Y, double B, double H, double Drehung) Lage(XElement e, Matrix eltern, Dictionary<string, string> g, double? standardHoehe = null)
    {
        var m = Transformation(e, eltern);
        var x = LaengeAttr(e, Svg + "x");
        var y = LaengeAttr(e, Svg + "y");
        var b = LaengeAttr(e, Svg + "width");
        var h = Laenge((string?)e.Attribute(Svg + "height")) ?? Laenge(g.GetValueOrDefault("min-height")) ?? standardHoehe ?? 0;
        if (b <= 0)
            b = Laenge(g.GetValueOrDefault("min-width")) ?? 0;
        var (cx, cy) = m.Anwenden(x + b / 2, y + h / 2);
        b *= m.SkalierungX;
        h *= m.SkalierungY;
        var winkel = m.DrehwinkelGrad;
        winkel = (winkel % 360 + 360) % 360;
        if (winkel > 359.99 || winkel < 0.01)
            winkel = 0;
        return (cx - b / 2, cy - h / 2, b, h, Math.Round(winkel, 2));
    }

    private void Rahmen(XElement e, Matrix eltern, List<Objekt> ziel)
    {
        var g = Aufloesen("graphic", (string?)e.Attribute(Draw + "style-name"), s => s.Grafik, true);

        var textBox = e.Element(Draw + "text-box");
        var tabelle = e.Element(Tabellen + "table");
        var bild = e.Element(Draw + "image");

        // Tabellen enthalten zusätzlich ein Vorschaubild; maßgeblich ist die Tabelle selbst.
        if (tabelle != null)
        {
            TabelleLesen(e, tabelle, g, eltern, ziel);
            return;
        }

        if (bild != null)
        {
            BildLesen(e, bild, g, eltern, ziel);
            return;
        }

        if (textBox != null)
        {
            var lage = Lage(e, eltern, g);
            var hoehe = lage.H;
            // LibreOffice vergrößert Textrahmen, wenn der Text nicht hineinpasst. Die ursprüngliche Rahmenhöhe steht in "min-height";
            // Publisher blendet überlaufenden Text aus, deshalb gilt diese Höhe.
            if (_ausPublisher && lage.Drehung == 0 && Laenge(g.GetValueOrDefault("min-height")) is { } mindest && mindest > 1 && hoehe > mindest * 1.05)
                hoehe = mindest;
            var feld = new TextfeldObjekt
            {
                X = lage.X, Y = lage.Y, Breite = lage.B, Hoehe = hoehe, Drehung = lage.Drehung,
                Innenabstand = Math.Round(Laenge(g.GetValueOrDefault("padding-left")) ?? 2.5, 2),
                VertikaleAusrichtung = VertikalAus(g),
            };
            FlaecheAnwenden(feld, g);
            feld.Absaetze = LeseText(textBox, e, g);
            ziel.Add(feld);
            return;
        }

        if (e.Element(Draw + "object") != null || e.Element(Draw + "object-ole") != null || e.Element(Draw + "plugin") != null)
            Hinweis("Eingebettete Objekte (z. B. Diagramme) werden nicht unterstützt.");
    }

    // ---------------------------------------------------------------- Tabellen

    private double Breiten(IEnumerable<double> werte, int von, int anzahl) => werte.Skip(von).Take(anzahl).Sum();

    /// <summary>Wandelt eine Tabelle in eine Gruppe von Textfeldern um (eine Zelle = ein Textfeld), weil Seitenwerk keine Tabellen kennt.</summary>
    private void TabelleLesen(XElement rahmen, XElement tabelle, Dictionary<string, string> g, Matrix eltern, List<Objekt> ziel)
    {
        var lage = Lage(rahmen, eltern, g);

        var spalten = new List<double>();
        foreach (var spalte in tabelle.Descendants(Tabellen + "table-column"))
        {
            var wiederholt = int.TryParse((string?)spalte.Attribute(Tabellen + "number-columns-repeated"), out var n) ? Math.Clamp(n, 1, 200) : 1;
            var eigenschaften = Aufloesen("table-column", (string?)spalte.Attribute(Tabellen + "style-name"), st => st.Tabelle, false);
            var breite = Laenge(eigenschaften.GetValueOrDefault("column-width")) ?? 0;
            for (var i = 0; i < wiederholt; i++)
                spalten.Add(breite);
        }

        var zeilen = tabelle.Descendants(Tabellen + "table-row").ToList();
        if (spalten.Count == 0 || zeilen.Count == 0)
        {
            Hinweis("Eine leere Tabelle wurde ausgelassen.");
            return;
        }
        if (spalten.All(b => b <= 0))
            spalten = spalten.Select(_ => lage.B / spalten.Count).ToList();

        var hoehen = new List<double>();
        foreach (var zeile in zeilen)
        {
            var eigenschaften = Aufloesen("table-row", (string?)zeile.Attribute(Tabellen + "style-name"), st => st.Tabelle, false);
            var h = Laenge(eigenschaften.GetValueOrDefault("row-height")) ?? Laenge(eigenschaften.GetValueOrDefault("min-row-height")) ?? lage.H / zeilen.Count;
            var wiederholt = int.TryParse((string?)zeile.Attribute(Tabellen + "number-rows-repeated"), out var n) ? Math.Clamp(n, 1, 200) : 1;
            if (wiederholt > 1)
                Hinweis("Wiederholte Tabellenzeilen wurden nur einmal übernommen.");
            hoehen.Add(h);
        }

        var zellen = new List<Objekt>();
        var y = lage.Y;
        for (var zeilenNr = 0; zeilenNr < zeilen.Count; zeilenNr++)
        {
            var spaltenNr = 0;
            var zeilenStil = (string?)zeilen[zeilenNr].Attribute(Tabellen + "default-cell-style-name");
            foreach (var zelle in zeilen[zeilenNr].Elements())
            {
                var wiederholt = int.TryParse((string?)zelle.Attribute(Tabellen + "number-columns-repeated"), out var rep) ? Math.Clamp(rep, 1, 200) : 1;
                if (zelle.Name != Tabellen + "table-cell")
                {
                    spaltenNr += wiederholt; // verdeckte Zelle einer verbundenen Zelle
                    continue;
                }

                var spannBreite = int.TryParse((string?)zelle.Attribute(Tabellen + "number-columns-spanned"), out var cs) ? Math.Max(1, cs) : 1;
                var spannHoehe = int.TryParse((string?)zelle.Attribute(Tabellen + "number-rows-spanned"), out var rs) ? Math.Max(1, rs) : 1;
                var stilName = (string?)zelle.Attribute(Tabellen + "style-name") ?? zeilenStil;
                var zellStil = Aufloesen("table-cell", stilName, st => st.Tabelle, false);

                for (var k = 0; k < wiederholt; k++)
                {
                    var feld = ZelleLesen(zelle, zellStil, rahmen, g);
                    if (feld != null)
                    {
                        feld.X = lage.X + Breiten(spalten, 0, spaltenNr);
                        feld.Y = y;
                        feld.Breite = Breiten(spalten, spaltenNr, spannBreite);
                        feld.Hoehe = Breiten(hoehen, zeilenNr, spannHoehe);
                        zellen.Add(feld);
                    }
                    spaltenNr += spannBreite;
                }
            }
            y += hoehen[zeilenNr];
        }

        Hinweis("Tabellen wurden in Textfelder umgewandelt (eine Zelle = ein Textfeld).");
        if (zellen.Count == 1)
            ziel.Add(zellen[0]);
        else if (zellen.Count > 1)
            ziel.Add(GruppeObjekt.Aus(zellen).Mit(t => t.Name = "Tabelle"));
    }

    private TextfeldObjekt? ZelleLesen(XElement zelle, Dictionary<string, string> stil, XElement rahmen, Dictionary<string, string> g)
    {
        var feld = new TextfeldObjekt
        {
            Innenabstand = Math.Round(Laenge(stil.GetValueOrDefault("padding-left")) ?? Laenge(stil.GetValueOrDefault("padding")) ?? 1, 2),
            VertikaleAusrichtung = stil.GetValueOrDefault("vertical-align") switch
            {
                "middle" => VertikaleAusrichtung.Mitte,
                "bottom" => VertikaleAusrichtung.Unten,
                _ => VertikaleAusrichtung.Oben,
            },
        };

        var fuellung = Farbe(stil.GetValueOrDefault("background-color"));
        if (fuellung == null && stil.GetValueOrDefault("fill") == "solid")
            fuellung = Farbe(stil.GetValueOrDefault("fill-color"));
        feld.Fuellung = fuellung;

        foreach (var schluessel in new[] { "border", "border-top", "border-left", "border-bottom", "border-right" })
        {
            if (!stil.TryGetValue(schluessel, out var rand) || rand == "none")
                continue;
            var teile = rand.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var breite = teile.Select(t => Punkt(t)).FirstOrDefault(w => w != null);
            var farbe = teile.Select(Farbe).FirstOrDefault(f => f != null);
            if (teile.Contains("none") || teile.Contains("hidden"))
                continue;
            feld.Rahmenfarbe = farbe ?? "#000000";
            feld.Rahmenbreite = Math.Max(0.25, Math.Round(breite ?? 0.75, 2));
            break;
        }

        feld.Absaetze = LeseText(zelle, rahmen, g);
        return feld.HatText || feld.Fuellung != null || feld.Rahmenfarbe != null ? feld : null;
    }

    private static VertikaleAusrichtung VertikalAus(Dictionary<string, string> g) =>
        g.GetValueOrDefault("textarea-vertical-align") switch
        {
            "middle" => VertikaleAusrichtung.Mitte,
            "bottom" => VertikaleAusrichtung.Unten,
            _ => VertikaleAusrichtung.Oben,
        };

    private void TextForm(XElement e, Matrix eltern, List<Objekt> ziel)
    {
        var g = Aufloesen("graphic", (string?)e.Attribute(Draw + "style-name"), s => s.Grafik, true);
        var lage = Lage(e, eltern, g);

        var form = new FormObjekt
        {
            X = lage.X, Y = lage.Y, Breite = lage.B, Hoehe = lage.H, Drehung = lage.Drehung,
            Innenabstand = Math.Round(Laenge(g.GetValueOrDefault("padding-left")) ?? 2.5, 2),
            VertikaleAusrichtung = VertikalAus(g),
        };

        switch (e.Name.LocalName)
        {
            case "rect":
                var radius = Laenge((string?)e.Attribute(Draw + "corner-radius")) ?? Laenge((string?)e.Attribute(Svg + "rx"));
                if (radius is > 0)
                {
                    form.Form = FormArt.AbgerundetesRechteck;
                    form.Eckenradius = Math.Round(radius.Value, 2);
                }
                break;
            case "ellipse":
            case "circle":
                form.Form = FormArt.Ellipse;
                break;
            case "custom-shape":
                var typ = (string?)e.Element(Draw + "enhanced-geometry")?.Attribute(Draw + "type");
                form.Form = typ switch
                {
                    "rectangle" or "quad-bevel" => FormArt.Rechteck,
                    "round-rectangle" or "round-quadrat" => FormArt.AbgerundetesRechteck,
                    "ellipse" => FormArt.Ellipse,
                    "diamond" => FormArt.Raute,
                    "isosceles-triangle" => FormArt.Dreieck,
                    "right-triangle" => FormArt.RechtwinkligesDreieck,
                    "pentagon" or "regular-pentagon" => FormArt.Fuenfeck,
                    "hexagon" => FormArt.Sechseck,
                    "right-arrow" or "arrow" => FormArt.Pfeil,
                    "star5" or "star" => FormArt.Stern,
                    _ => FormArt.Rechteck,
                };
                if (form.Form == FormArt.Rechteck && typ is not (null or "rectangle" or "quad-bevel"))
                    Hinweis($"Die Form „{typ}“ wird nur als Rechteck dargestellt.");
                break;
        }

        FlaecheAnwenden(form, g);
        form.Absaetze = LeseText(e, e, g);
        ziel.Add(form);
    }

    private void Linie(XElement e, Matrix eltern, List<Objekt> ziel)
    {
        var g = Aufloesen("graphic", (string?)e.Attribute(Draw + "style-name"), s => s.Grafik, true);
        var m = Transformation(e, eltern);
        var (x1, y1) = m.Anwenden(LaengeAttr(e, Svg + "x1"), LaengeAttr(e, Svg + "y1"));
        var (x2, y2) = m.Anwenden(LaengeAttr(e, Svg + "x2"), LaengeAttr(e, Svg + "y2"));

        var linie = LinieObjekt.Zwischen(x1, y1, x2, y2);
        linie.Farbe = Farbe(g.GetValueOrDefault("stroke-color")) ?? "#000000";
        linie.Staerke = Math.Max(0.25, Math.Round(Punkt(g.GetValueOrDefault("stroke-width")) ?? 0, 2));
        var strich = g.GetValueOrDefault("stroke");
        linie.Art = strich == "dash" ? StrichartAus(g.GetValueOrDefault("stroke-dash")) : Strichart.Durchgezogen;
        linie.Anfang = Spitze(g.GetValueOrDefault("marker-start"));
        linie.Ende = Spitze(g.GetValueOrDefault("marker-end"));
        ziel.Add(linie);
    }

    private static Pfeilspitze Spitze(string? marker)
    {
        if (string.IsNullOrEmpty(marker))
            return Pfeilspitze.Keine;
        return marker.Contains("Circle", StringComparison.OrdinalIgnoreCase) ? Pfeilspitze.Kreis : Pfeilspitze.Pfeil;
    }

    private static readonly Regex ZahlenRegex = new(@"-?\d*\.?\d+(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

    private void Pfad(XElement e, Matrix eltern, List<Objekt> ziel)
    {
        var g = Aufloesen("graphic", (string?)e.Attribute(Draw + "style-name"), s => s.Grafik, true);
        var lage = Lage(e, eltern, g);

        var viewBox = ((string?)e.Attribute(Svg + "viewBox") ?? "")
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN)
            .ToArray();
        if (viewBox.Length != 4 || viewBox.Any(double.IsNaN) || viewBox[2] <= 0 || viewBox[3] <= 0)
        {
            Hinweis("Ein Pfad ohne gültige Größenangabe wurde ausgelassen.");
            return;
        }

        string daten;
        var punkte = (string?)e.Attribute(Draw + "points");
        var d = (string?)e.Attribute(Svg + "d");
        if (e.Name.LocalName is "polygon" or "polyline" && punkte != null)
        {
            var zahlen = ZahlenRegex.Matches(punkte).Select(m => m.Value).ToArray();
            var sb = new StringBuilder();
            for (var i = 0; i + 1 < zahlen.Length; i += 2)
                sb.Append(i == 0 ? "M " : " L ").Append(zahlen[i]).Append(' ').Append(zahlen[i + 1]);
            if (e.Name.LocalName == "polygon")
                sb.Append(" Z");
            daten = sb.ToString();
        }
        else if (d != null)
        {
            daten = d;
        }
        else
        {
            return;
        }

        var pfad = new PfadObjekt
        {
            X = lage.X, Y = lage.Y, Breite = lage.B, Hoehe = lage.H, Drehung = lage.Drehung,
            Daten = daten, ViewX = viewBox[0], ViewY = viewBox[1], ViewBreite = viewBox[2], ViewHoehe = viewBox[3],
        };
        FlaecheAnwenden(pfad, g, fuellungErlaubt: e.Name.LocalName != "polyline");
        ziel.Add(pfad);
    }

    private void Gruppe(XElement e, Matrix eltern, List<Objekt> ziel, bool istMuster)
    {
        var m = Transformation(e, eltern);
        var kinder = new List<Objekt>();
        foreach (var kind in e.Elements())
            Verarbeite(kind, m, kinder, istMuster);
        if (kinder.Count == 1)
            ziel.Add(kinder[0]);
        else if (kinder.Count > 1)
            ziel.Add(GruppeObjekt.Aus(kinder));
    }

    // ---------------------------------------------------------------- Bilder

    private static readonly HashSet<string> UnterstuetzteBildformate = new() { "png", "jpg", "gif", "bmp", "tif", "emf", "wmf" };

    /// <summary>Liest ein Bild aus dem Paket (oder eingebetteten Base64-Daten) und legt es als Medium an. Unbekannte Formate ergeben null und einen Hinweis.</summary>
    private Medium? LadeMedium(string? href, string? base64)
    {
        byte[]? daten = null;
        var endung = "";

        if (!string.IsNullOrWhiteSpace(base64))
        {
            try
            {
                daten = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                daten = null;
            }
        }
        else if (!string.IsNullOrEmpty(href))
        {
            var pfad = href.TrimStart('.', '/');
            var eintrag = _zip.GetEntry(pfad);
            if (eintrag != null)
            {
                using var s = eintrag.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                daten = ms.ToArray();
                endung = Path.GetExtension(pfad).TrimStart('.').ToLowerInvariant();
            }
        }

        if (daten == null || daten.Length == 0)
        {
            Hinweis("Ein Bild konnte nicht aus der Datei gelesen werden (verknüpfte Bilder werden nicht unterstützt).");
            return null;
        }

        var format = BildFormat.Erkennen(daten) ?? endung;
        if (format == "jpeg")
            format = "jpg";
        if (!UnterstuetzteBildformate.Contains(format))
        {
            Hinweis($"Grafiken im Format „{(format.Length > 0 ? format.ToUpperInvariant() : "unbekannt")}“ werden nicht unterstützt und wurden ausgelassen.");
            return null;
        }

        var schluessel = href ?? Convert.ToHexString(daten.AsSpan(0, Math.Min(32, daten.Length)));
        if (!_medien.TryGetValue(schluessel, out var medium))
        {
            medium = _dokument.MediumHinzufuegen(daten, format);
            _medien[schluessel] = medium;
        }
        return medium;
    }

    private void BildLesen(XElement rahmen, XElement bildElement, Dictionary<string, string> g, Matrix eltern, List<Objekt> ziel)
    {
        var lage = Lage(rahmen, eltern, g);
        var medium = LadeMedium((string?)bildElement.Attribute(Xlink + "href"), bildElement.Element(Office + "binary-data")?.Value);
        if (medium == null)
            return;
        var daten = medium.Daten;

        var bild = new BildObjekt
        {
            X = lage.X, Y = lage.Y, Breite = lage.B, Hoehe = lage.H, Drehung = lage.Drehung,
            MedienId = medium.Id,
            Seitenverhaeltnis = lage.H > 0 ? lage.B / lage.H : 1,
        };

        // Beschnitt: fo:clip="rect(oben, rechts, unten, links)" in Längen, bezogen auf die Originalgröße des Bilds.
        var clip = g.GetValueOrDefault("clip");
        if (clip != null && clip.StartsWith("rect(") && BildFormat.Groesse(daten) is { } info)
        {
            var teile = clip[5..].TrimEnd(')').Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(t => Laenge(t) ?? 0).ToArray();
            if (teile.Length == 4 && teile.Any(t => t != 0))
            {
                var breiteMm = info.BreitePixel / info.DpiX * 25.4;
                var hoeheMm = info.HoehePixel / info.DpiY * 25.4;
                bild.BeschnittOben = Math.Clamp(teile[0] / hoeheMm, 0, 0.95);
                bild.BeschnittRechts = Math.Clamp(teile[1] / breiteMm, 0, 0.95);
                bild.BeschnittUnten = Math.Clamp(teile[2] / hoeheMm, 0, 0.95);
                bild.BeschnittLinks = Math.Clamp(teile[3] / breiteMm, 0, 0.95);
            }
        }

        var rahmenfarbe = g.GetValueOrDefault("stroke");
        if (rahmenfarbe is "solid" or "dash")
            FlaecheAnwenden(bild, g, fuellungErlaubt: false);
        else
            bild.Rahmenfarbe = null;

        ziel.Add(bild);
    }

    // ---------------------------------------------------------------- Text

    private List<Absatz> LeseText(XElement behaelter, XElement formElement, Dictionary<string, string> g)
    {
        var textStilName = (string?)formElement.Attribute(Draw + "text-style-name");
        var basisText = new Dictionary<string, string>();
        var basisAbsatz = new Dictionary<string, string>();
        if (_standard.TryGetValue("graphic", out var std))
            Mischen(basisText, std.Text);
        // Textanteile des Grafikstils des Objekts
        var grafikStilName = (string?)formElement.Attribute(Draw + "style-name");
        Mischen(basisText, Aufloesen("graphic", grafikStilName, s => s.Text, false));
        Mischen(basisAbsatz, Aufloesen("graphic", grafikStilName, s => s.Absatz, false));
        Mischen(basisText, Aufloesen("paragraph", textStilName, s => s.Text, false));
        Mischen(basisAbsatz, Aufloesen("paragraph", textStilName, s => s.Absatz, false));

        var liste = new List<Absatz>();
        SammleAbsaetze(behaelter, basisText, basisAbsatz, liste, null);
        if (liste.Count == 0)
        {
            var leer = new Absatz();
            leer.Laeufe.Add(ZuLauf(basisText, ""));
            ApplyAbsatzEigenschaften(leer, basisAbsatz);
            liste.Add(leer);
        }
        return liste;
    }

    private void SammleAbsaetze(XElement behaelter, Dictionary<string, string> basisText, Dictionary<string, string> basisAbsatz, List<Absatz> liste, string? praefix)
    {
        foreach (var kind in behaelter.Elements())
        {
            if (kind.Name == Text + "p" || kind.Name == Text + "h")
            {
                liste.Add(LeseAbsatz(kind, basisText, basisAbsatz, praefix));
            }
            else if (kind.Name == Text + "list")
            {
                foreach (var eintrag in kind.Elements(Text + "list-item"))
                    SammleAbsaetze(eintrag, basisText, basisAbsatz, liste, "• ");
            }
        }
    }

    private Absatz LeseAbsatz(XElement p, Dictionary<string, string> basisText, Dictionary<string, string> basisAbsatz, string? praefix)
    {
        var stilName = (string?)p.Attribute(Text + "style-name");
        var tp = new Dictionary<string, string>(basisText);
        Mischen(tp, Aufloesen("paragraph", stilName, s => s.Text, false));
        var pp = new Dictionary<string, string>(basisAbsatz);
        Mischen(pp, Aufloesen("paragraph", stilName, s => s.Absatz, false));

        var absatz = new Absatz();
        ApplyAbsatzEigenschaften(absatz, pp);
        if (praefix != null)
            absatz.Laeufe.Add(ZuLauf(tp, praefix));
        SammleLaeufe(p, tp, absatz.Laeufe);
        if (absatz.Laeufe.Count == 0)
            absatz.Laeufe.Add(ZuLauf(tp, ""));
        absatz.Bereinigen();
        return absatz;
    }

    private static void ApplyAbsatzEigenschaften(Absatz absatz, Dictionary<string, string> pp)
    {
        absatz.Ausrichtung = pp.GetValueOrDefault("text-align") switch
        {
            "center" => Ausrichtung.Zentriert,
            "end" or "right" => Ausrichtung.Rechts,
            "justify" => Ausrichtung.Blocksatz,
            _ => Ausrichtung.Links,
        };

        var zeilenhoehe = pp.GetValueOrDefault("line-height");
        if (zeilenhoehe != null && zeilenhoehe.EndsWith('%')
            && double.TryParse(zeilenhoehe.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var prozent))
            absatz.Zeilenabstand = Math.Clamp(prozent / 100, 0.5, 4);

        if (Punkt(pp.GetValueOrDefault("margin-bottom")) is { } nach and > 0)
            absatz.AbstandNach = Math.Round(nach, 1);
    }

    private static readonly Regex WeissraumRegex = new(@"[ \t\r\n]+", RegexOptions.Compiled);

    private void SammleLaeufe(XElement element, Dictionary<string, string> tp, List<Textlauf> laeufe)
    {
        foreach (var knoten in element.Nodes())
        {
            switch (knoten)
            {
                case XText t:
                {
                    var text = WeissraumRegex.Replace(t.Value, " ");
                    if (text.Length > 0)
                        laeufe.Add(ZuLauf(tp, text));
                    break;
                }
                case XElement el when el.Name == Text + "span":
                {
                    var span = new Dictionary<string, string>(tp);
                    Mischen(span, Aufloesen("text", (string?)el.Attribute(Text + "style-name"), s => s.Text, false));
                    SammleLaeufe(el, span, laeufe);
                    break;
                }
                case XElement el when el.Name == Text + "s":
                {
                    var anzahl = int.TryParse((string?)el.Attribute(Text + "c"), out var c) ? Math.Clamp(c, 1, 500) : 1;
                    laeufe.Add(ZuLauf(tp, new string(' ', anzahl)));
                    break;
                }
                case XElement el when el.Name == Text + "tab":
                    laeufe.Add(ZuLauf(tp, "\t"));
                    break;
                case XElement el when el.Name == Text + "line-break":
                    laeufe.Add(ZuLauf(tp, "\n"));
                    break;
                case XElement el when el.Name == Text + "soft-page-break":
                    break;
                case XElement el:
                    SammleLaeufe(el, tp, laeufe); // Verweise, Felder usw.: nur den Text übernehmen
                    break;
            }
        }
    }

    private static readonly Dictionary<string, string> SchriftErsatz = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Liberation Serif"] = "Times New Roman",
        ["Liberation Sans"] = "Arial",
        ["Liberation Mono"] = "Courier New",
        ["Carlito"] = "Calibri",
        ["Caladea"] = "Cambria",
        ["DejaVu Sans"] = "Segoe UI",
        ["Noto Sans"] = "Segoe UI",
    };

    private Textlauf ZuLauf(Dictionary<string, string> tp, string text)
    {
        var lauf = new Textlauf { Text = text };

        string? schrift = null;
        if (tp.TryGetValue("font-name", out var fontName))
            schrift = _schriften.GetValueOrDefault(fontName, fontName);
        else if (tp.TryGetValue("font-family", out var familie))
            schrift = familie.Trim('\'', '"');
        if (!string.IsNullOrWhiteSpace(schrift))
            lauf.Schriftart = SchriftErsatz.GetValueOrDefault(schrift, schrift);

        if (Punkt(tp.GetValueOrDefault("font-size")) is { } groesse and > 0)
            lauf.Groesse = Math.Round(groesse, 1);

        var gewicht = tp.GetValueOrDefault("font-weight");
        lauf.Fett = gewicht == "bold" || (int.TryParse(gewicht, out var w) && w >= 600);
        var kursiv = tp.GetValueOrDefault("font-style");
        lauf.Kursiv = kursiv is "italic" or "oblique";
        var unterstrich = tp.GetValueOrDefault("text-underline-style");
        lauf.Unterstrichen = unterstrich != null && unterstrich != "none";
        var durchgestrichen = tp.GetValueOrDefault("text-line-through-style");
        lauf.Durchgestrichen = durchgestrichen != null && durchgestrichen != "none";
        lauf.Farbe = Farbe(tp.GetValueOrDefault("color")) ?? "#000000";
        return lauf;
    }
}
