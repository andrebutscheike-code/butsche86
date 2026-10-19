using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Controls.Ribbon;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Seitenwerk.Core.Bearbeiten;
using Seitenwerk.Core.Dateiformat;
using Seitenwerk.Core.Import;
using Seitenwerk.Core.Modell;
using Seitenwerk.Dialoge;
using Seitenwerk.Steuerung;
using Seitenwerk.Werkzeuge;

namespace Seitenwerk;

public partial class MainWindow
{
    private readonly Dictionary<string, Action> _befehle = new();
    private readonly Dictionary<string, Action<bool>> _schalter = new();
    private readonly List<(string Praefix, Action<string> Aktion)> _praefixBefehle = new();
    private readonly Dictionary<string, List<UIElement>> _nachTag = new();

    private static CultureInfo Invariant => CultureInfo.InvariantCulture;

    // ---------------------------------------------------------------- Ausführung

    private void Befehl(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag })
            return;
        e.Handled = true;
        Ausfuehren(() => FuehreAus(tag));
    }

    private void Umschalter(object sender, RoutedEventArgs e)
    {
        if (_intern || sender is not FrameworkElement { Tag: string tag })
            return;
        var an = sender switch
        {
            ToggleButton knopf => knopf.IsChecked == true,
            RibbonMenuItem eintrag => eintrag.IsChecked,
            _ => false,
        };
        Ausfuehren(() =>
        {
            if (_schalter.TryGetValue(tag, out var aktion))
                aktion(an);
        });
    }

    public void FuehreAus(string tag)
    {
        if (_befehle.TryGetValue(tag, out var aktion))
        {
            aktion();
            return;
        }
        foreach (var (praefix, aktionMitRest) in _praefixBefehle)
        {
            if (tag.StartsWith(praefix, StringComparison.Ordinal))
            {
                aktionMitRest(tag[praefix.Length..]);
                return;
            }
        }
    }

    /// <summary>Führt einen Befehl aus und meldet erwartbare Fehler (Dateien, Import) als Meldung statt als Absturz.</summary>
    private void Ausfuehren(Action aktion)
    {
        try
        {
            aktion();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DateiFehler or ImportFehler or NotSupportedException)
        {
            MessageBox.Show(this, ex.Message, "Seitenwerk", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static double Zahl(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    private RichTextBearbeitung? AktiveBox => Editor.Textbearbeitung as RichTextBearbeitung;

    private void BaueBefehle()
    {
        var b = _befehle;

        // Datei
        b["Datei.Neu"] = () => _ = DateiNeuAsync();
        b["Datei.Oeffnen"] = () => _ = DateiOeffnenDialogAsync();
        b["Datei.Speichern"] = () => Speichern();
        b["Datei.SpeichernUnter"] = () => SpeichernUnter();
        b["Datei.Pdf"] = () => PdfExportieren();
        b["Datei.Png"] = () => PngExportieren();
        b["Datei.Drucken"] = () => Drucken();
        b["Datei.LibreOffice"] = () => LibreOfficeWaehlen();
        b["Datei.Info"] = () => ZeigeInfo();
        b["Datei.Beenden"] = () => Close();

        // Bearbeiten
        b["Bearbeiten.Rueckgaengig"] = () => Editor.Rueckgaengig();
        b["Bearbeiten.Wiederholen"] = () => Editor.Wiederholen();
        b["Bearbeiten.Ausschneiden"] = () =>
        {
            if (AktiveBox is { } box) ApplicationCommands.Cut.Execute(null, box.Box); else Editor.Ausschneiden();
        };
        b["Bearbeiten.Kopieren"] = () =>
        {
            if (AktiveBox is { } box) ApplicationCommands.Copy.Execute(null, box.Box); else Editor.Kopieren();
        };
        b["Bearbeiten.Einfuegen"] = () =>
        {
            if (AktiveBox is { } box) ApplicationCommands.Paste.Execute(null, box.Box); else Editor.Einfuegen();
        };
        b["Bearbeiten.Duplizieren"] = () => Editor.AuswahlDuplizieren();
        b["Bearbeiten.Loeschen"] = () => Editor.AuswahlLoeschen();

        // Werkzeuge und Einfügen
        b["Werkzeug.Textfeld"] = () => WerkzeugWechseln(Werkzeug.Textfeld);
        b["Werkzeug.Linie"] = () => WerkzeugWechseln(Werkzeug.Linie);
        b["Werkzeug.Pfeil"] = () => WerkzeugWechseln(Werkzeug.Pfeil);
        b["Einfuegen.Bild"] = () => BildEinfuegenDialog();

        // Schrift
        _schalter["Format.Fett"] = an => Editor.TextFormatAnwenden(new TextFormat { Fett = an });
        _schalter["Format.Kursiv"] = an => Editor.TextFormatAnwenden(new TextFormat { Kursiv = an });
        _schalter["Format.Unterstrichen"] = an => Editor.TextFormatAnwenden(new TextFormat { Unterstrichen = an });
        _schalter["Format.Durchgestrichen"] = an => Editor.TextFormatAnwenden(new TextFormat { Durchgestrichen = an });
        b["Schrift.Groesser"] = () => Editor.TextFormatAnwenden(new TextFormat { GroesseAendern = SchrittGroesse(+1) });
        b["Schrift.Kleiner"] = () => Editor.TextFormatAnwenden(new TextFormat { GroesseAendern = SchrittGroesse(-1) });
        b["Farbe.Schrift"] = () => Editor.TextFormatAnwenden(new TextFormat { Farbe = _letzteSchriftfarbe });

        // Absatz
        _schalter["Absatz.Links"] = _ => Editor.TextFormatAnwenden(new TextFormat { Ausrichtung = Ausrichtung.Links });
        _schalter["Absatz.Zentriert"] = _ => Editor.TextFormatAnwenden(new TextFormat { Ausrichtung = Ausrichtung.Zentriert });
        _schalter["Absatz.Rechts"] = _ => Editor.TextFormatAnwenden(new TextFormat { Ausrichtung = Ausrichtung.Rechts });
        _schalter["Absatz.Blocksatz"] = _ => Editor.TextFormatAnwenden(new TextFormat { Ausrichtung = Ausrichtung.Blocksatz });
        _praefixBefehle.Add(("Absatz.Zeile.", rest => Editor.TextFormatAnwenden(new TextFormat { Zeilenabstand = Zahl(rest) })));
        b["Text.Oben"] = () => VertikalSetzen(VertikaleAusrichtung.Oben);
        b["Text.Mitte"] = () => VertikalSetzen(VertikaleAusrichtung.Mitte);
        b["Text.Unten"] = () => VertikalSetzen(VertikaleAusrichtung.Unten);

        // Füllung und Linien
        b["Farbe.Fuellung"] = () => Editor.Fuellung(_letzteFuellfarbe);
        b["Farbe.Linie"] = () => Editor.Rahmenfarbe(_letzteLinienfarbe);
        b["Farbe.Hintergrund"] = () => Editor.SeitenHintergrund(_letzteHintergrund);
        _praefixBefehle.Add(("Linie.Staerke.", rest => Editor.Rahmenbreite(Zahl(rest))));
        _praefixBefehle.Add(("Linie.Art.", rest => Editor.Rahmenart(Enum.Parse<Strichart>(rest))));
        b["Linie.Pfeil.Keine"] = () => Editor.Pfeilspitzen(Pfeilspitze.Keine, Pfeilspitze.Keine);
        b["Linie.Pfeil.Ende"] = () => Editor.Pfeilspitzen(Pfeilspitze.Keine, Pfeilspitze.Pfeil);
        b["Linie.Pfeil.Beide"] = () => Editor.Pfeilspitzen(Pfeilspitze.Pfeil, Pfeilspitze.Pfeil);

        // Anordnen
        b["Anordnen.Vordergrund"] = () => Editor.InDenVordergrund();
        b["Anordnen.Hintergrund"] = () => Editor.InDenHintergrund();
        b["Anordnen.Vor"] = () => Editor.EineEbeneNachVorn();
        b["Anordnen.Zurueck"] = () => Editor.EineEbeneNachHinten();
        b["Anordnen.Gruppieren"] = () => Editor.Gruppieren();
        b["Anordnen.Aufheben"] = () => Editor.GruppierungAufheben();
        b["Anordnen.Sperren"] = () => Editor.SperreUmschalten();
        _praefixBefehle.Add(("Ausrichten.", rest => Editor.Ausrichten(Enum.Parse<AusrichtenArt>(rest), _ausrichtenAnSeite ? Bezugsrahmen.Seite : Bezugsrahmen.Auswahl)));
        b["Verteilen.Horizontal"] = () => Editor.Verteilen(true);
        b["Verteilen.Vertikal"] = () => Editor.Verteilen(false);
        b["Drehen.Rechts"] = () => Editor.Drehen(90);
        b["Drehen.Links"] = () => Editor.Drehen(-90);
        b["Drehen.Null"] = () => DrehungZuruecksetzen();
        _schalter["Ausrichten.AnSeite"] = an => _ausrichtenAnSeite = an;

        // Seiten
        b["Seite.Neu"] = () => Editor.SeiteEinfuegen(Editor.AktuelleSeite + 1);
        b["Seite.Duplizieren"] = () => Editor.SeiteDuplizieren(Editor.AktuelleSeite);
        b["Seite.Loeschen"] = () => SeiteLoeschen();
        b["Seite.Vor"] = () => Editor.SeiteVerschieben(Editor.AktuelleSeite, Editor.AktuelleSeite - 1);
        b["Seite.Zurueck"] = () => Editor.SeiteVerschieben(Editor.AktuelleSeite, Editor.AktuelleSeite + 1);
        b["Seite.Einrichten"] = () => SeiteEinrichtenDialogAnzeigen();
        b["Seite.Hoch"] = () => SeitenAusrichtung(false);
        b["Seite.Quer"] = () => SeitenAusrichtung(true);
        _praefixBefehle.Add(("Raender.", rest => RaenderSetzen(Zahl(rest))));

        // Ansicht
        b["Zoom.GanzeSeite"] = () => ZoomAnpassen(ZoomModus.GanzeSeite);
        b["Zoom.Seitenbreite"] = () => ZoomAnpassen(ZoomModus.Seitenbreite);
        b["Zoom.100"] = () => ZoomMitErhaltDerMitte(1.0);
        b["Zoom.Plus"] = () => ZoomMitErhaltDerMitte(Editor.Zoom * 1.25);
        b["Zoom.Minus"] = () => ZoomMitErhaltDerMitte(Editor.Zoom / 1.25);
        _schalter["Ansicht.Lineale"] = an => Editor.ZeigeLineale = an;
        _schalter["Ansicht.Navigation"] = an => Editor.ZeigeNavigation = an;
        _schalter["Ansicht.Raender"] = an => Editor.ZeigeRaender = an;
        _schalter["Ansicht.Raster"] = an => Editor.ZeigeRaster = an;
        _schalter["Fang.Objekt"] = an => Editor.ObjektFang = an;
        _schalter["Fang.Raster"] = an => Editor.RasterEinrasten = an;
        _schalter["Pruefen.Rechtschreibung"] = an => Editor.Rechtschreibung = an;
    }

    private string _letzteHintergrund = "#FFF2CC";

    /// <summary>Befehle, die im Menüband stehen, aber nicht belegt sind (für den Selbsttest).</summary>
    internal IReadOnlyList<string> NichtZugeordneteBefehle()
    {
        var ergebnis = new List<string>();
        foreach (var (tag, elemente) in _nachTag)
        {
            var istSchalter = elemente.Any(e => e is ToggleButton || e is RibbonMenuItem { IsCheckable: true });
            var belegt = _befehle.ContainsKey(tag) || _schalter.ContainsKey(tag) || _praefixBefehle.Any(p => tag.StartsWith(p.Praefix, StringComparison.Ordinal));
            if (!belegt)
                ergebnis.Add(tag);
        }
        return ergebnis;
    }

    private double SchrittGroesse(int richtung)
    {
        var aktuell = Editor.LeseTextFormat()?.Groesse ?? 12;
        var schritt = aktuell >= 10 ? 2.0 : 1.0;
        return richtung * schritt;
    }

    private void WerkzeugWechseln(Werkzeug werkzeug)
    {
        Editor.BeendeTextbearbeitung();
        Editor.AktivesWerkzeug = Editor.AktivesWerkzeug == werkzeug ? Werkzeug.Auswahl : werkzeug;
    }

    private void VertikalSetzen(VertikaleAusrichtung ausrichtung)
    {
        Editor.BeendeTextbearbeitung();
        var ziele = Editor.Auswahl.OfType<TextTraeger>().ToList();
        if (ziele.Count == 0)
            return;
        foreach (var t in ziele)
            t.VertikaleAusrichtung = ausrichtung;
        Editor.Abschliessen();
    }

    private void DrehungZuruecksetzen()
    {
        Editor.BeendeTextbearbeitung();
        var ziele = Editor.Auswahl.Where(o => o is not GruppeObjekt).ToList();
        if (ziele.Count == 0)
            return;
        foreach (var o in ziele)
            o.Drehung = 0;
        Editor.Abschliessen();
    }

    // ---------------------------------------------------------------- Seiten

    private void SeiteLoeschen()
    {
        if (Editor.Dokument.Seiten.Count <= 1)
        {
            ZeigeMeldung("Die letzte Seite kann nicht gelöscht werden.");
            return;
        }
        var seite = Editor.AktuelleSeiteModell;
        if (seite != null && seite.Objekte.Count > 0
            && MessageBox.Show(this, $"Seite {Editor.AktuelleSeite + 1} samt Inhalt löschen?", "Seitenwerk",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        Editor.SeiteLoeschen(Editor.AktuelleSeite);
    }

    private void SeiteEinrichtenDialogAnzeigen()
    {
        Editor.BeendeTextbearbeitung();
        var d = Editor.Dokument;
        var dialog = new SeiteEinrichtenDialog("Seite einrichten", d.SeitenBreite, d.SeitenHoehe, d.Raender) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            Editor.SeiteEinrichten(dialog.Breite, dialog.Hoehe, dialog.Raender);
            ZoomAnpassen(ZoomModus.GanzeSeite);
        }
    }

    private void SeitenAusrichtung(bool quer)
    {
        var d = Editor.Dokument;
        var istQuer = d.SeitenBreite > d.SeitenHoehe;
        if (istQuer == quer)
            return;
        Editor.SeiteEinrichten(d.SeitenHoehe, d.SeitenBreite, d.Raender);
        ZoomAnpassen(ZoomModus.GanzeSeite);
    }

    private void RaenderSetzen(double mm)
    {
        var d = Editor.Dokument;
        Editor.SeiteEinrichten(d.SeitenBreite, d.SeitenHoehe, new Seitenraender { Oben = mm, Unten = mm, Links = mm, Rechts = mm });
    }

    private void BildEinfuegenDialog()
    {
        Editor.BeendeTextbearbeitung();
        var dialog = new OpenFileDialog
        {
            Title = "Bild einfügen",
            Filter = "Bilder|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.emf;*.wmf|Alle Dateien|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != true)
            return;
        var seite = Editor.AktuelleSeiteModell;
        if (seite == null)
            return;
        var eingefuegt = 0;
        foreach (var datei in dialog.FileNames)
        {
            var bild = Editor.BildEinfuegen(File.ReadAllBytes(datei), seite);
            if (bild == null)
                MessageBox.Show(this, $"„{Path.GetFileName(datei)}“ ist kein unterstütztes Bildformat.", "Seitenwerk", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                eingefuegt++;
        }
        if (eingefuegt > 0)
            ZeigeMeldung(eingefuegt == 1 ? "Bild eingefügt." : $"{eingefuegt} Bilder eingefügt.");
    }

    // ---------------------------------------------------------------- Menüband: Aufbau

    private void SammleElemente()
    {
        _nachTag.Clear();
        void Besuche(object? knoten)
        {
            if (knoten is FrameworkElement { Tag: string tag } fe)
            {
                if (!_nachTag.TryGetValue(tag, out var liste))
                    _nachTag[tag] = liste = new List<UIElement>();
                liste.Add(fe);
            }
            if (knoten is DependencyObject d)
            {
                foreach (var kind in LogicalTreeHelper.GetChildren(d))
                    Besuche(kind);
            }
        }
        Besuche(Menueband);
        Besuche(Menueband.QuickAccessToolBar);
        Besuche(Menueband.ApplicationMenu);
    }

    private void BaueMenueband()
    {
        // Schriftarten und -größen
        var schriften = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        SchriftartKategorie.ItemsSource = schriften;
        var groessen = new[] { 6, 7, 8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 22, 24, 28, 32, 36, 40, 48, 56, 64, 72, 96, 120, 144, 200 };
        GroesseKategorie.ItemsSource = groessen.Select(g => g.ToString("0.#", CultureInfo.CurrentCulture)).ToList();

        // Farbmenüs
        BaueFarbMenue(SchriftfarbeKnopf, hex => { _letzteSchriftfarbe = hex ?? "#000000"; Editor.TextFormatAnwenden(new TextFormat { Farbe = _letzteSchriftfarbe }); }, null, _letzteSchriftfarbe);
        BaueFarbMenue(FuellfarbeKnopf, hex => { if (hex != null) _letzteFuellfarbe = hex; Editor.Fuellung(hex); }, "Keine Füllung", _letzteFuellfarbe);
        BaueFarbMenue(LinienfarbeKnopf, hex => { if (hex != null) _letzteLinienfarbe = hex; Editor.Rahmenfarbe(hex); }, "Keine Linie", _letzteLinienfarbe);
        BaueFarbMenue(HintergrundKnopf, hex => { if (hex != null) { _letzteHintergrund = hex; Editor.SeitenHintergrund(hex); } }, null, _letzteHintergrund);

        // Formen
        BaueFormenMenue(FormenMenueStart);
        BaueFormenMenue(FormenMenueEinfuegen);

        // Seitengrößen
        foreach (var format in Seitenformat.Standard)
        {
            var eintrag = new RibbonMenuItem { Header = format.Name };
            var f = format;
            eintrag.Click += (_, _) => Ausfuehren(() =>
            {
                var d = Editor.Dokument;
                var quer = d.SeitenBreite > d.SeitenHoehe;
                Editor.SeiteEinrichten(quer ? f.Hoehe : f.Breite, quer ? f.Breite : f.Hoehe, d.Raender);
                ZoomAnpassen(ZoomModus.GanzeSeite);
            });
            FormatMenue.Items.Add(eintrag);
        }
        FormatMenue.Items.Add(new RibbonSeparator());
        var weitere = new RibbonMenuItem { Header = "Weitere Papierformate …" };
        weitere.Click += (_, _) => Ausfuehren(SeiteEinrichtenDialogAnzeigen);
        FormatMenue.Items.Add(weitere);
    }

    private void BaueFarbMenue(RibbonSplitButton knopf, Action<string?> setzen, string? keineText, string startFarbe)
    {
        knopf.Items.Clear();
        if (keineText != null)
        {
            var keine = new RibbonMenuItem { Header = keineText };
            keine.Click += (_, _) => Ausfuehren(() => setzen(null));
            knopf.Items.Add(keine);
        }

        var galerie = new RibbonGallery { MaxColumnCount = 7 };
        var kategorie = new RibbonGalleryCategory { Header = "Farben", MaxColumnCount = 7 };
        foreach (var hex in Farben_Palette())
        {
            var feld = new Border
            {
                Width = 20,
                Height = 20,
                Background = Darstellung.Farben.Pinsel(hex),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
            };
            kategorie.Items.Add(new RibbonGalleryItem { Content = feld, Tag = null, ToolTip = hex, DataContext = hex });
        }
        galerie.Items.Add(kategorie);
        galerie.SelectionChanged += (_, e) =>
        {
            if (_intern)
                return;
            if (e.NewValue is RibbonGalleryItem { DataContext: string hex })
            {
                _intern = true;
                galerie.SelectedItem = null;
                _intern = false;
                knopf.IsDropDownOpen = false;
                Ausfuehren(() => setzen(hex));
            }
        };
        knopf.Items.Add(galerie);

        knopf.Items.Add(new RibbonSeparator());
        var weitere = new RibbonMenuItem { Header = "Weitere Farben …" };
        weitere.Click += (_, _) => Ausfuehren(() =>
        {
            var dialog = new FarbDialog(startFarbe) { Owner = this };
            if (dialog.ShowDialog() == true)
                setzen(dialog.Farbe);
        });
        knopf.Items.Add(weitere);
    }

    private static IEnumerable<string> Farben_Palette() => Darstellung.Farben.Palette;

    private static string FormName(FormArt art) => art switch
    {
        FormArt.Rechteck => "Rechteck",
        FormArt.AbgerundetesRechteck => "Abgerundetes Rechteck",
        FormArt.Ellipse => "Ellipse",
        FormArt.Dreieck => "Dreieck",
        FormArt.RechtwinkligesDreieck => "Rechtwinkliges Dreieck",
        FormArt.Raute => "Raute",
        FormArt.Fuenfeck => "Fünfeck",
        FormArt.Sechseck => "Sechseck",
        FormArt.Pfeil => "Pfeil nach rechts",
        FormArt.Stern => "Stern",
        _ => art.ToString(),
    };

    private void BaueFormenMenue(RibbonMenuButton menue)
    {
        var galerie = new RibbonGallery { MaxColumnCount = 5 };
        var kategorie = new RibbonGalleryCategory { Header = "Grundformen", MaxColumnCount = 5 };
        foreach (var art in Enum.GetValues<FormArt>())
        {
            var bild = new Image { Source = Symbole.Form(art), Width = 28, Height = 28, Margin = new Thickness(2) };
            kategorie.Items.Add(new RibbonGalleryItem { Content = bild, ToolTip = FormName(art), DataContext = art });
        }
        galerie.Items.Add(kategorie);
        galerie.SelectionChanged += (_, e) =>
        {
            if (_intern)
                return;
            if (e.NewValue is RibbonGalleryItem { DataContext: FormArt art })
            {
                _intern = true;
                galerie.SelectedItem = null;
                _intern = false;
                menue.IsDropDownOpen = false;
                Editor.BeendeTextbearbeitung();
                // Ist eine Form markiert, wird deren Gestalt geändert; sonst wird zum Zeichnen eine neue Form vorbereitet.
                if (Editor.Auswahl.Any(o => o is FormObjekt))
                {
                    Editor.FormAendern(art);
                }
                else
                {
                    Editor.NeueForm = art;
                    Editor.AktivesWerkzeug = Werkzeug.Form;
                }
            }
        };
        menue.Items.Add(galerie);
    }

    // ---------------------------------------------------------------- Menüband: Zustand

    private void SetzeAktiv(string tag, bool aktiv)
    {
        if (!_nachTag.TryGetValue(tag, out var liste))
            return;
        foreach (var element in liste)
            element.IsEnabled = aktiv;
    }

    private void AktualisiereMenueband()
    {
        if (_nachTag.Count == 0)
            return;
        _intern = true;
        try
        {
            var auswahl = Editor.Auswahl;
            var hat = auswahl.Count > 0;

            SetzeAktiv("Bearbeiten.Rueckgaengig", Editor.KannRueckgaengig);
            SetzeAktiv("Bearbeiten.Wiederholen", Editor.KannWiederholen);
            foreach (var tag in new[] { "Bearbeiten.Ausschneiden", "Bearbeiten.Kopieren", "Bearbeiten.Duplizieren", "Bearbeiten.Loeschen",
                         "Anordnen.Vordergrund", "Anordnen.Hintergrund", "Anordnen.Vor", "Anordnen.Zurueck", "Anordnen.Sperren",
                         "Drehen.Rechts", "Drehen.Links", "Drehen.Null", "Farbe.Fuellung", "Farbe.Linie" })
                SetzeAktiv(tag, hat || Editor.Textbearbeitung != null);
            SetzeAktiv("Anordnen.Gruppieren", auswahl.Count > 1);
            SetzeAktiv("Anordnen.Aufheben", auswahl.Any(o => o is GruppeObjekt));
            SetzeAktiv("Verteilen.Horizontal", auswahl.Count > 2);
            SetzeAktiv("Verteilen.Vertikal", auswahl.Count > 2);
            foreach (var art in Enum.GetNames<AusrichtenArt>())
                SetzeAktiv("Ausrichten." + art, hat);
            SetzeAktiv("Seite.Loeschen", Editor.Dokument.Seiten.Count > 1);
            SetzeAktiv("Seite.Vor", Editor.AktuelleSeite > 0);
            SetzeAktiv("Seite.Zurueck", Editor.AktuelleSeite < Editor.Dokument.Seiten.Count - 1);

            var werkzeug = Editor.AktivesWerkzeug;
            TextfeldKnopfStart.IsChecked = TextfeldKnopfEinfuegen.IsChecked = werkzeug == Werkzeug.Textfeld;
            LinieKnopfStart.IsChecked = LinieKnopfEinfuegen.IsChecked = werkzeug == Werkzeug.Linie;
            PfeilKnopfStart.IsChecked = PfeilKnopfEinfuegen.IsChecked = werkzeug == Werkzeug.Pfeil;

            LinealKnopf.IsChecked = Editor.ZeigeLineale;
            NavigationKnopf.IsChecked = Editor.ZeigeNavigation;
            RaenderKnopf.IsChecked = Editor.ZeigeRaender;
            RasterKnopf.IsChecked = Editor.ZeigeRaster;
            ObjektFangKnopf.IsChecked = Editor.ObjektFang;
            RasterFangKnopf.IsChecked = Editor.RasterEinrasten;
            RechtschreibKnopf.IsChecked = Editor.Rechtschreibung;
        }
        finally
        {
            _intern = false;
        }
    }

    private void AktualisiereTextFormat()
    {
        if (_nachTag.Count == 0)
            return;
        var z = Editor.LeseTextFormat();
        _intern = true;
        try
        {
            FettKnopf.IsChecked = z?.Fett == true;
            KursivKnopf.IsChecked = z?.Kursiv == true;
            UnterstrichenKnopf.IsChecked = z?.Unterstrichen == true;
            DurchgestrichenKnopf.IsChecked = z?.Durchgestrichen == true;
            LinksKnopf.IsChecked = z?.Ausrichtung == Ausrichtung.Links;
            ZentriertKnopf.IsChecked = z?.Ausrichtung == Ausrichtung.Zentriert;
            RechtsKnopf.IsChecked = z?.Ausrichtung == Ausrichtung.Rechts;
            BlocksatzKnopf.IsChecked = z?.Ausrichtung == Ausrichtung.Blocksatz;
            if (!SchriftartBox.IsKeyboardFocusWithin)
                SchriftartBox.Text = z?.Schriftart ?? "";
            if (!GroesseBox.IsKeyboardFocusWithin)
                GroesseBox.Text = z?.Groesse?.ToString("0.#", CultureInfo.CurrentCulture) ?? "";
        }
        finally
        {
            _intern = false;
        }
    }

    // ---------------------------------------------------------------- Schriftart und -größe

    private void SchriftartGewaehlt(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_intern || e.NewValue is not string name)
            return;
        _intern = true;
        SchriftartGalerie.SelectedItem = null;
        _intern = false;
        SchriftartBox.Text = name;
        Ausfuehren(() => Editor.TextFormatAnwenden(new TextFormat { Schriftart = name }));
    }

    private void SchriftartTaste(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        var name = SchriftartBox.Text.Trim();
        if (name.Length == 0)
            return;
        e.Handled = true;
        Ausfuehren(() => Editor.TextFormatAnwenden(new TextFormat { Schriftart = name }));
    }

    private void GroesseGewaehlt(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_intern || e.NewValue is not string text)
            return;
        _intern = true;
        GroesseGalerie.SelectedItem = null;
        _intern = false;
        GroesseBox.Text = text;
        WendeGroesseAn(text);
    }

    private void GroesseTaste(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        WendeGroesseAn(GroesseBox.Text);
    }

    private void WendeGroesseAn(string text)
    {
        var ok = double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var wert)
                 || double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out wert);
        if (!ok || wert < 1 || wert > 999)
        {
            ZeigeMeldung("Die Schriftgröße muss zwischen 1 und 999 Punkt liegen.");
            return;
        }
        Ausfuehren(() => Editor.TextFormatAnwenden(new TextFormat { Groesse = wert }));
    }
}
