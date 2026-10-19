using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Seitenwerk.Core.Modell;
using Seitenwerk.Darstellung;

namespace Seitenwerk.Steuerung;

/// <summary>Zugriff des Menübands auf ein gerade bearbeitetes Textfeld.</summary>
public interface ITextBearbeitung
{
    TextTraeger Objekt { get; }
    void Anwenden(TextFormat format);
    TextFormatZustand LeseZustand();
    void Beenden(bool uebernehmen);
    event Action? AuswahlGeaendert;
    bool EnthaeltFokus { get; }
}

/// <summary>Umwandlung zwischen dem Textmodell und WPF-FlowDocument für die Bearbeitung im Textfeld.</summary>
public static class TextKonverter
{
    private const double DipProPunkt = 96.0 / 72.0;

    public static FlowDocument ZuFlowDocument(IEnumerable<Absatz> absaetze)
    {
        var dokument = new FlowDocument { PagePadding = new Thickness(0), LineStackingStrategy = LineStackingStrategy.MaxHeight };
        foreach (var absatz in absaetze)
        {
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, absatz.AbstandNach * DipProPunkt),
                TextAlignment = absatz.Ausrichtung switch
                {
                    Ausrichtung.Zentriert => TextAlignment.Center,
                    Ausrichtung.Rechts => TextAlignment.Right,
                    Ausrichtung.Blocksatz => TextAlignment.Justify,
                    _ => TextAlignment.Left,
                },
            };

            var erster = absatz.Laeufe.FirstOrDefault() ?? new Textlauf();
            paragraph.FontFamily = TextLayout.Schrift(erster.Schriftart);
            paragraph.FontSize = erster.Groesse * DipProPunkt;

            foreach (var lauf in absatz.Laeufe)
            {
                var teile = lauf.Text.Split('\n');
                for (var i = 0; i < teile.Length; i++)
                {
                    if (i > 0)
                        paragraph.Inlines.Add(new LineBreak());
                    if (teile[i].Length == 0)
                        continue;
                    var run = new Run(teile[i])
                    {
                        FontFamily = TextLayout.Schrift(lauf.Schriftart),
                        FontSize = lauf.Groesse * DipProPunkt,
                        FontWeight = lauf.Fett ? FontWeights.Bold : FontWeights.Normal,
                        FontStyle = lauf.Kursiv ? FontStyles.Italic : FontStyles.Normal,
                        Foreground = Farben.Pinsel(lauf.Farbe) ?? Brushes.Black,
                    };
                    var dekor = new TextDecorationCollection();
                    if (lauf.Unterstrichen)
                        dekor.Add(TextDecorations.Underline[0]);
                    if (lauf.Durchgestrichen)
                        dekor.Add(TextDecorations.Strikethrough[0]);
                    if (dekor.Count > 0)
                        run.TextDecorations = dekor;
                    paragraph.Inlines.Add(run);
                }
            }
            if (absatz.Zeilenabstand > 1.001)
            {
                var groesste = absatz.Laeufe.Count > 0 ? absatz.Laeufe.Max(l => l.Groesse) : Textlauf.StandardGroesse;
                paragraph.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                paragraph.LineHeight = groesste * DipProPunkt * TextLayout.Schrift(erster.Schriftart).LineSpacing * absatz.Zeilenabstand;
            }
            dokument.Blocks.Add(paragraph);
        }
        if (dokument.Blocks.Count == 0)
            dokument.Blocks.Add(new Paragraph());
        return dokument;
    }

    public static List<Absatz> AusFlowDocument(FlowDocument dokument, Textlauf vorlage)
    {
        var ergebnis = new List<Absatz>();
        foreach (var block in dokument.Blocks)
            SammleBlock(block, ergebnis, vorlage);
        if (ergebnis.Count == 0)
        {
            var leer = new Absatz();
            leer.Laeufe.Add(vorlage.Kopie());
            ergebnis.Add(leer);
        }
        return ergebnis;
    }

    private static void SammleBlock(Block block, List<Absatz> ziel, Textlauf vorlage)
    {
        switch (block)
        {
            case Paragraph paragraph:
                ziel.Add(LeseParagraph(paragraph, vorlage));
                break;
            case List liste:
                foreach (var eintrag in liste.ListItems)
                    foreach (var b in eintrag.Blocks)
                        SammleBlock(b, ziel, vorlage);
                break;
            case Section abschnitt:
                foreach (var b in abschnitt.Blocks)
                    SammleBlock(b, ziel, vorlage);
                break;
        }
    }

    private static Absatz LeseParagraph(Paragraph paragraph, Textlauf vorlage)
    {
        var absatz = new Absatz
        {
            Ausrichtung = paragraph.TextAlignment switch
            {
                TextAlignment.Center => Ausrichtung.Zentriert,
                TextAlignment.Right => Ausrichtung.Rechts,
                TextAlignment.Justify => Ausrichtung.Blocksatz,
                _ => Ausrichtung.Links,
            },
            AbstandNach = Math.Round(paragraph.Margin.Bottom / DipProPunkt, 1),
        };

        var standard = LaufAusElement(paragraph, vorlage);
        foreach (var inline in paragraph.Inlines)
            SammleInline(inline, absatz.Laeufe, standard);

        if (!double.IsNaN(paragraph.LineHeight) && paragraph.LineHeight > 0 && absatz.Laeufe.Count > 0)
        {
            var groesste = absatz.Laeufe.Max(l => l.Groesse);
            var natuerlich = groesste * DipProPunkt * TextLayout.Schrift(absatz.Laeufe[0].Schriftart).LineSpacing;
            if (natuerlich > 0)
                absatz.Zeilenabstand = Math.Round(Math.Clamp(paragraph.LineHeight / natuerlich, 0.5, 4), 2);
        }

        if (absatz.Laeufe.Count == 0)
            absatz.Laeufe.Add(standard);
        absatz.Bereinigen();
        return absatz;
    }

    private static void SammleInline(Inline inline, List<Textlauf> laeufe, Textlauf standard)
    {
        switch (inline)
        {
            case LineBreak:
            {
                var umbruch = standard.Kopie();
                umbruch.Text = "\n";
                laeufe.Add(umbruch);
                break;
            }
            case Run run:
            {
                var lauf = LaufAusElement(run, standard);
                lauf.Text = run.Text;
                if (lauf.Text.Length > 0)
                    laeufe.Add(lauf);
                break;
            }
            case Span span:
                foreach (var kind in span.Inlines)
                    SammleInline(kind, laeufe, LaufAusElement(span, standard));
                break;
        }
    }

    private static Textlauf LaufAusElement(TextElement element, Textlauf vorlage)
    {
        var lauf = vorlage.Kopie();
        lauf.Schriftart = element.FontFamily?.Source ?? vorlage.Schriftart;
        lauf.Groesse = Math.Round(element.FontSize / DipProPunkt, 1);
        lauf.Fett = element.FontWeight.ToOpenTypeWeight() >= 600;
        lauf.Kursiv = element.FontStyle == FontStyles.Italic || element.FontStyle == FontStyles.Oblique;
        if (element.Foreground is SolidColorBrush pinsel)
            lauf.Farbe = Farben.Hex(pinsel.Color);
        var dekor = (element as Inline)?.TextDecorations;
        lauf.Unterstrichen = dekor != null && dekor.Any(d => d.Location == TextDecorationLocation.Underline);
        lauf.Durchgestrichen = dekor != null && dekor.Any(d => d.Location == TextDecorationLocation.Strikethrough);
        return lauf;
    }
}

/// <summary>Bearbeitung eines Textfelds in einer RichTextBox über dem Seitenbild.</summary>
public sealed class RichTextBearbeitung : ITextBearbeitung
{
    private const double DipProPunkt = 96.0 / 72.0;
    private readonly RichTextBox _box;
    private readonly Action<RichTextBearbeitung, bool> _beendet;
    private bool _beendetFlag;

    public RichTextBearbeitung(TextTraeger objekt, RichTextBox box, Action<RichTextBearbeitung, bool> beendet)
    {
        Objekt = objekt;
        _box = box;
        _beendet = beendet;
        _box.SelectionChanged += (_, _) => AuswahlGeaendert?.Invoke();
        _box.LostKeyboardFocus += BoxVerliertFokus;
        _box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
                FokusZurueck = true;
                Beenden(true);
            }
        };
    }

    public TextTraeger Objekt { get; }
    public event Action? AuswahlGeaendert;
    public bool EnthaeltFokus => _box.IsKeyboardFocusWithin;

    public RichTextBox Box => _box;

    /// <summary>Gesetzt, wenn die Bearbeitung mit Esc endete und die Seite den Fokus wieder übernehmen soll.</summary>
    public bool FokusZurueck { get; private set; }

    private void BoxVerliertFokus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        // Das Menüband darf den Fokus übernehmen, ohne die Bearbeitung zu beenden.
        if (e.NewFocus is DependencyObject neu && (IstTeilVonMenueband(neu) || IstNachfahre(_box, neu)))
            return;
        Beenden(true);
    }

    private static bool IstTeilVonMenueband(DependencyObject element)
    {
        for (var e = element; e != null; e = VisualTreeHelper.GetParent(e) ?? LogicalTreeHelper.GetParent(e))
        {
            if (e is System.Windows.Controls.Ribbon.Ribbon or System.Windows.Controls.Primitives.Popup)
                return true;
        }
        return false;
    }

    private static bool IstNachfahre(DependencyObject vorfahre, DependencyObject element)
    {
        for (var e = element; e != null; e = VisualTreeHelper.GetParent(e) ?? LogicalTreeHelper.GetParent(e))
        {
            if (ReferenceEquals(e, vorfahre))
                return true;
        }
        return false;
    }

    public void Beenden(bool uebernehmen)
    {
        if (_beendetFlag)
            return;
        _beendetFlag = true;
        _box.LostKeyboardFocus -= BoxVerliertFokus;
        _beendet(this, uebernehmen);
    }

    public List<Absatz> LeseAbsaetze(Textlauf vorlage) => TextKonverter.AusFlowDocument(_box.Document, vorlage);

    public void Anwenden(TextFormat f)
    {
        var auswahl = _box.Selection;

        if (f.Schriftart != null)
            auswahl.ApplyPropertyValue(TextElement.FontFamilyProperty, TextLayout.Schrift(f.Schriftart));
        if (f.Groesse != null)
            auswahl.ApplyPropertyValue(TextElement.FontSizeProperty, Math.Clamp(f.Groesse.Value, 1, 999) * DipProPunkt);
        if (f.GroesseAendern != null)
        {
            // Jeden Textabschnitt einzeln ändern, weil die Auswahl unterschiedliche Größen enthalten kann.
            var aktuell = auswahl.GetPropertyValue(TextElement.FontSizeProperty);
            if (aktuell is double dip)
                auswahl.ApplyPropertyValue(TextElement.FontSizeProperty, Math.Clamp(dip + f.GroesseAendern.Value * DipProPunkt, DipProPunkt, 999 * DipProPunkt));
            else
                foreach (var run in RunsInAuswahl(auswahl))
                    run.FontSize = Math.Clamp(run.FontSize + f.GroesseAendern.Value * DipProPunkt, DipProPunkt, 999 * DipProPunkt);
        }
        if (f.Fett != null)
            auswahl.ApplyPropertyValue(TextElement.FontWeightProperty, f.Fett.Value ? FontWeights.Bold : FontWeights.Normal);
        if (f.Kursiv != null)
            auswahl.ApplyPropertyValue(TextElement.FontStyleProperty, f.Kursiv.Value ? FontStyles.Italic : FontStyles.Normal);
        if (f.Unterstrichen != null || f.Durchgestrichen != null)
        {
            var vorhanden = auswahl.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            var neu = new TextDecorationCollection();
            var unter = f.Unterstrichen ?? (vorhanden?.Any(d => d.Location == TextDecorationLocation.Underline) ?? false);
            var durch = f.Durchgestrichen ?? (vorhanden?.Any(d => d.Location == TextDecorationLocation.Strikethrough) ?? false);
            if (unter)
                neu.Add(TextDecorations.Underline[0]);
            if (durch)
                neu.Add(TextDecorations.Strikethrough[0]);
            auswahl.ApplyPropertyValue(Inline.TextDecorationsProperty, neu);
        }
        if (f.Farbe != null && Farben.Pinsel(f.Farbe) is { } pinsel)
            auswahl.ApplyPropertyValue(TextElement.ForegroundProperty, pinsel);

        if (f.Ausrichtung != null)
        {
            var ausrichtung = f.Ausrichtung.Value switch
            {
                Ausrichtung.Zentriert => TextAlignment.Center,
                Ausrichtung.Rechts => TextAlignment.Right,
                Ausrichtung.Blocksatz => TextAlignment.Justify,
                _ => TextAlignment.Left,
            };
            auswahl.ApplyPropertyValue(Block.TextAlignmentProperty, ausrichtung);
        }

        if (f.Zeilenabstand != null)
        {
            foreach (var paragraph in ParagraphenInAuswahl(auswahl))
            {
                var groesste = paragraph.Inlines.OfType<Run>().Select(r => r.FontSize).DefaultIfEmpty(paragraph.FontSize).Max();
                if (Math.Abs(f.Zeilenabstand.Value - 1) < 0.001)
                {
                    paragraph.ClearValue(Block.LineHeightProperty);
                    paragraph.ClearValue(Paragraph.LineStackingStrategyProperty);
                }
                else
                {
                    paragraph.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                    paragraph.LineHeight = groesste * paragraph.FontFamily.LineSpacing * f.Zeilenabstand.Value;
                }
            }
        }

        _box.Focus();
    }

    private static IEnumerable<Run> RunsInAuswahl(TextRange auswahl)
    {
        var start = auswahl.Start;
        var ende = auswahl.End;
        var runs = new List<Run>();
        for (var zeiger = start; zeiger != null && zeiger.CompareTo(ende) < 0; zeiger = zeiger.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (zeiger.Parent is Run run && !runs.Contains(run))
                runs.Add(run);
        }
        return runs;
    }

    private static IEnumerable<Paragraph> ParagraphenInAuswahl(TextRange auswahl)
    {
        var ergebnis = new List<Paragraph>();
        var start = auswahl.Start.Paragraph;
        var ende = auswahl.End.Paragraph;
        if (start == null)
            return ergebnis;
        var aktuell = (Block?)start;
        while (aktuell != null)
        {
            if (aktuell is Paragraph p)
                ergebnis.Add(p);
            if (aktuell == ende)
                break;
            aktuell = aktuell.NextBlock;
        }
        return ergebnis;
    }

    public TextFormatZustand LeseZustand()
    {
        var auswahl = _box.Selection;

        string? Schrift()
        {
            var wert = auswahl.GetPropertyValue(TextElement.FontFamilyProperty);
            return wert is FontFamily f ? f.Source : null;
        }

        double? Groesse()
        {
            var wert = auswahl.GetPropertyValue(TextElement.FontSizeProperty);
            return wert is double d ? Math.Round(d / DipProPunkt, 1) : null;
        }

        bool? Gewicht()
        {
            var wert = auswahl.GetPropertyValue(TextElement.FontWeightProperty);
            return wert is FontWeight w ? w.ToOpenTypeWeight() >= 600 : null;
        }

        bool? Stil()
        {
            var wert = auswahl.GetPropertyValue(TextElement.FontStyleProperty);
            return wert is FontStyle s ? s == FontStyles.Italic || s == FontStyles.Oblique : null;
        }

        bool? Dekor(TextDecorationLocation ort)
        {
            var wert = auswahl.GetPropertyValue(Inline.TextDecorationsProperty);
            return wert is TextDecorationCollection c ? c.Any(d => d.Location == ort) : null;
        }

        string? Farbe()
        {
            var wert = auswahl.GetPropertyValue(TextElement.ForegroundProperty);
            return wert is SolidColorBrush p ? Farben.Hex(p.Color) : null;
        }

        Ausrichtung? Ausricht()
        {
            var wert = auswahl.GetPropertyValue(Block.TextAlignmentProperty);
            if (wert is not TextAlignment a)
                return null;
            return a switch
            {
                TextAlignment.Center => Core.Modell.Ausrichtung.Zentriert,
                TextAlignment.Right => Core.Modell.Ausrichtung.Rechts,
                TextAlignment.Justify => Core.Modell.Ausrichtung.Blocksatz,
                _ => Core.Modell.Ausrichtung.Links,
            };
        }

        return new TextFormatZustand(Schrift(), Groesse(), Gewicht(), Stil(),
            Dekor(TextDecorationLocation.Underline), Dekor(TextDecorationLocation.Strikethrough), Farbe(), Ausricht());
    }
}
