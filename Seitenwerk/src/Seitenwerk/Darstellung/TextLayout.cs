using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Seitenwerk.Core.Modell;

namespace Seitenwerk.Darstellung;

/// <summary>Setzt die Absätze eines Textfelds mit WPF-FormattedText. Alle Längen in Millimetern, Schriftgrößen in Punkt.</summary>
public static class TextLayout
{
    public const double MmProPunkt = 25.4 / 72;

    private static readonly Dictionary<string, FontFamily> Schriften = new();

    public static FontFamily Schrift(string name)
    {
        if (!Schriften.TryGetValue(name, out var familie))
        {
            familie = new FontFamily(string.IsNullOrWhiteSpace(name) ? Textlauf.StandardSchriftart : name);
            Schriften[name] = familie;
        }
        return familie;
    }

    public sealed record Zeile(FormattedText Text, double Hoehe, double AbstandNach);

    public static List<Zeile> Erstellen(IReadOnlyList<Absatz> absaetze, double breiteMm, double pixelsPerDip)
    {
        var ergebnis = new List<Zeile>(absaetze.Count);
        foreach (var absatz in absaetze)
            ergebnis.Add(ErstelleAbsatz(absatz, Math.Max(1, breiteMm), pixelsPerDip));
        return ergebnis;
    }

    public static double GesamtHoehe(IReadOnlyList<Zeile> zeilen) => zeilen.Sum(z => z.Hoehe + z.AbstandNach);

    private static Zeile ErstelleAbsatz(Absatz absatz, double breiteMm, double pixelsPerDip)
    {
        var laeufe = absatz.Laeufe.Count > 0 ? absatz.Laeufe : new List<Textlauf> { new() };
        var text = string.Concat(laeufe.Select(l => l.Text)).Replace("\t", "    ").Replace("\r", "");
        var leer = text.Length == 0;
        if (leer)
            text = " "; // damit auch leere Absätze ihre Zeilenhöhe haben

        var erster = laeufe[0];
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            Typeface(erster),
            erster.Groesse * MmProPunkt,
            Farben.Pinsel(erster.Farbe) ?? Brushes.Black,
            pixelsPerDip)
        {
            MaxTextWidth = breiteMm,
            TextAlignment = absatz.Ausrichtung switch
            {
                Ausrichtung.Zentriert => TextAlignment.Center,
                Ausrichtung.Rechts => TextAlignment.Right,
                Ausrichtung.Blocksatz => TextAlignment.Justify,
                _ => TextAlignment.Left,
            },
        };

        var position = 0;
        var groesste = 0.0;
        FontFamily? groessteSchrift = null;
        foreach (var lauf in laeufe)
        {
            var laenge = lauf.Text.Replace("\t", "    ").Replace("\r", "").Length;
            if (laenge > 0 && !leer)
            {
                ft.SetFontFamily(Schrift(lauf.Schriftart), position, laenge);
                ft.SetFontSize(lauf.Groesse * MmProPunkt, position, laenge);
                ft.SetFontWeight(lauf.Fett ? FontWeights.Bold : FontWeights.Normal, position, laenge);
                ft.SetFontStyle(lauf.Kursiv ? FontStyles.Italic : FontStyles.Normal, position, laenge);
                ft.SetForegroundBrush(Farben.Pinsel(lauf.Farbe) ?? Brushes.Black, position, laenge);
                if (lauf.Unterstrichen || lauf.Durchgestrichen)
                {
                    var dekor = new TextDecorationCollection();
                    if (lauf.Unterstrichen)
                        dekor.Add(TextDecorations.Underline[0]);
                    if (lauf.Durchgestrichen)
                        dekor.Add(TextDecorations.Strikethrough[0]);
                    ft.SetTextDecorations(dekor, position, laenge);
                }
                position += laenge;
            }
            if (lauf.Groesse > groesste)
            {
                groesste = lauf.Groesse;
                groessteSchrift = Schrift(lauf.Schriftart);
            }
        }

        if (Math.Abs(absatz.Zeilenabstand - 1.0) > 0.001 && groessteSchrift != null)
        {
            var natuerlich = groesste * MmProPunkt * groessteSchrift.LineSpacing;
            ft.LineHeight = natuerlich * absatz.Zeilenabstand;
        }

        return new Zeile(ft, ft.Height, absatz.AbstandNach * MmProPunkt);
    }

    private static Typeface Typeface(Textlauf lauf) => new(
        Schrift(lauf.Schriftart),
        lauf.Kursiv ? FontStyles.Italic : FontStyles.Normal,
        lauf.Fett ? FontWeights.Bold : FontWeights.Normal,
        FontStretches.Normal);
}
