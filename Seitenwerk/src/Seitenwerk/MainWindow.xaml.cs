using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Controls.Ribbon;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Seitenwerk.Core.Modell;
using Seitenwerk.Steuerung;
using Seitenwerk.Werkzeuge;

namespace Seitenwerk;

/// <summary>Eintrag der Seitennavigation.</summary>
public sealed class SeitenEintrag : INotifyPropertyChanged
{
    private ImageSource? _vorschau;
    private int _nummer;

    public int Nummer
    {
        get => _nummer;
        set
        {
            _nummer = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Nummer)));
        }
    }

    public ImageSource? Vorschau
    {
        get => _vorschau;
        set
        {
            _vorschau = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Vorschau)));
        }
    }

    /// <summary>Serialisierter Seiteninhalt, bei dem die Vorschau zuletzt erzeugt wurde.</summary>
    internal string? Stand { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class MainWindow : RibbonWindow
{
    private readonly Einstellungen _einstellungen;
    private readonly List<SeitenAnsicht> _ansichten = new();
    private readonly ObservableCollection<SeitenEintrag> _eintraege = new();
    private readonly DispatcherTimer _vorschauTimer;
    private readonly Lineal _linealOben = new() { Senkrecht = false };
    private readonly Lineal _linealLinks = new() { Senkrecht = true };
    private bool _intern;
    private bool _ausgeblendeteNavigation;
    private GridLength _navigationBreite = new(170);
    private string _letzteFuellfarbe = "#FFE699";
    private string _letzteLinienfarbe = "#000000";
    private string _letzteSchriftfarbe = "#C00000";
    private bool _ausrichtenAnSeite;

    public MainWindow() : this(null, null)
    {
    }

    public MainWindow(EditorSitzung? editor, string? dateiZumOeffnen)
    {
        InitializeComponent();
        _einstellungen = Einstellungen.Laden();
        Editor = editor ?? new EditorSitzung();
        Editor.Rechtschreibung = _einstellungen.Rechtschreibung;
        Editor.ZeigeLineale = _einstellungen.ZeigeLineale;
        Editor.ZeigeNavigation = _einstellungen.ZeigeNavigation;
        Editor.ObjektFang = _einstellungen.ObjektFang;

        SeitenListe.ItemsSource = _eintraege;
        LinealOben.Children.Add(_linealOben);
        LinealLinks.Children.Add(_linealLinks);
        _linealOben.Editor = _linealLinks.Editor = Editor;
        _linealOben.UrsprungQuelle = () => SeitenUrsprung();
        _linealLinks.UrsprungQuelle = () => SeitenUrsprung();

        _vorschauTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(450) };
        _vorschauTimer.Tick += (_, _) =>
        {
            _vorschauTimer.Stop();
            AktualisiereVorschauen();
        };

        BaueBefehle();
        BaueMenueband();
        VerbindeEditor();

        Loaded += (_, _) =>
        {
            SammleElemente();
            BaueSeitenAuf();
            ZoomAnpassen(ZoomModus.GanzeSeite);
            AktualisiereAlles();
            AktualisiereZuletzt();
            if (dateiZumOeffnen != null)
                _ = OeffneDateiAsync(dateiZumOeffnen, ohneNachfrage: true);
        };
        Bildlauf.PreviewMouseLeftButtonDown += (_, e) =>
        {
            // Ein Klick neben die Seiten beendet die Textbearbeitung.
            if (Editor.Textbearbeitung is RichTextBearbeitung r && e.OriginalSource is DependencyObject quelle && !IstInnerhalb(r.Box, quelle))
                Editor.BeendeTextbearbeitung();
        };
        Closing += FensterSchliesst;
        PreviewKeyDown += FensterTaste;
        SizeChanged += (_, _) => AktualisiereLineale();
    }

    private static bool IstInnerhalb(DependencyObject vorfahre, DependencyObject element)
    {
        for (var e = element; e != null; e = VisualTreeHelper.GetParent(e) ?? LogicalTreeHelper.GetParent(e))
        {
            if (ReferenceEquals(e, vorfahre))
                return true;
        }
        return false;
    }

    public EditorSitzung Editor { get; private set; }

    internal IReadOnlyList<SeitenAnsicht> Ansichten => _ansichten;

    // ---------------------------------------------------------------- Editor verbinden

    private void VerbindeEditor()
    {
        Editor.StrukturGeaendert += () =>
        {
            BaueSeitenAuf();
            AktualisiereAlles();
        };
        Editor.InhaltGeaendert += () =>
        {
            foreach (var a in _ansichten)
                a.Neuzeichnen();
            _vorschauTimer.Stop();
            _vorschauTimer.Start();
            AktualisiereStatus();
            AktualisiereLineale();
        };
        Editor.LageGeaendert += () =>
        {
            AktualisiereStatus();
            AktualisiereLineale();
        };
        Editor.AuswahlGeaendert += () =>
        {
            foreach (var a in _ansichten)
                a.ZeichneUeberlagerungNeu();
            AktualisiereMenueband();
            AktualisiereTextFormat();
            AktualisiereStatus();
            AktualisiereLineale();
        };
        Editor.TextAuswahlGeaendert += AktualisiereTextFormat;
        Editor.ZoomGeaendert += () =>
        {
            foreach (var a in _ansichten)
                a.Aktualisieren();
            _intern = true;
            ZoomRegler.Value = Editor.Zoom * 100;
            _intern = false;
            ZoomText.Text = $"{Editor.Zoom * 100:0} %";
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, AktualisiereLineale);
        };
        Editor.WerkzeugGeaendert += AktualisiereMenueband;
        Editor.ZustandGeaendert += () =>
        {
            AktualisiereTitel();
            AktualisiereMenueband();
            AktualisiereStatus();
            WaehleListenEintrag();
            AktualisiereLineale();
        };
        Editor.OptionenGeaendert += () =>
        {
            foreach (var a in _ansichten)
                a.Neuzeichnen();
            WendeAnsichtsoptionenAn();
            AktualisiereMenueband();
        };
    }

    /// <summary>Ersetzt die Sitzung (z. B. beim Selbsttest) und verbindet das Fenster neu.</summary>
    private void AktualisiereAlles()
    {
        AktualisiereTitel();
        WendeAnsichtsoptionenAn();
        AktualisiereMenueband();
        AktualisiereTextFormat();
        AktualisiereStatus();
        AktualisiereListe();
        AktualisiereLineale();
        ZoomText.Text = $"{Editor.Zoom * 100:0} %";
    }

    // ---------------------------------------------------------------- Seiten

    private void BaueSeitenAuf()
    {
        if (!IsLoaded)
            return;
        SeitenStapel.Children.Clear();
        _ansichten.Clear();
        for (var i = 0; i < Editor.Dokument.Seiten.Count; i++)
        {
            var ansicht = new SeitenAnsicht(Editor, i);
            var rahmen = new Border
            {
                Style = (Style)FindResource("SeitenRahmen"),
                Child = ansicht,
                HorizontalAlignment = HorizontalAlignment.Center,
                Tag = i,
            };
            SeitenStapel.Children.Add(rahmen);
            _ansichten.Add(ansicht);
        }
        AktualisiereListe();
    }

    private void AktualisiereListe()
    {
        var anzahl = Editor.Dokument.Seiten.Count;
        while (_eintraege.Count > anzahl)
            _eintraege.RemoveAt(_eintraege.Count - 1);
        while (_eintraege.Count < anzahl)
            _eintraege.Add(new SeitenEintrag());
        for (var i = 0; i < anzahl; i++)
            _eintraege[i].Nummer = i + 1;
        _vorschauTimer.Stop();
        _vorschauTimer.Start();
        WaehleListenEintrag();
    }

    private void AktualisiereVorschauen()
    {
        var dokument = Editor.Dokument;
        for (var i = 0; i < dokument.Seiten.Count && i < _eintraege.Count; i++)
        {
            var seite = dokument.Seiten[i];
            var stand = JsonSerializer.Serialize(seite, ModellJson.Optionen) + $"|{dokument.SeitenBreite}|{dokument.SeitenHoehe}";
            if (_eintraege[i].Stand == stand && _eintraege[i].Vorschau != null)
                continue;
            _eintraege[i].Vorschau = Exporter.Vorschau(dokument, seite, 240);
            _eintraege[i].Stand = stand;
        }
    }

    private void WaehleListenEintrag()
    {
        if (_eintraege.Count == 0)
            return;
        var index = Math.Clamp(Editor.AktuelleSeite, 0, _eintraege.Count - 1);
        if (SeitenListe.SelectedIndex == index)
            return;
        _intern = true;
        SeitenListe.SelectedIndex = index;
        SeitenListe.ScrollIntoView(SeitenListe.SelectedItem);
        _intern = false;
    }

    private void SeiteInListeGewaehlt(object sender, SelectionChangedEventArgs e)
    {
        if (_intern || SeitenListe.SelectedIndex < 0)
            return;
        ZeigeSeite(SeitenListe.SelectedIndex);
    }

    public void ZeigeSeite(int index)
    {
        if (index < 0 || index >= _ansichten.Count)
            return;
        Editor.AktuelleSeite = index;
        if (SeitenStapel.Children[index] is FrameworkElement rahmen)
            rahmen.BringIntoView();
    }

    private Point? SeitenUrsprung()
    {
        var index = Editor.AktuelleSeite;
        if (index < 0 || index >= _ansichten.Count || !_ansichten[index].IsLoaded)
            return null;
        try
        {
            return _ansichten[index].TransformToAncestor(Bildlauf).Transform(new Point(0, 0));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void AktualisiereLineale()
    {
        _linealOben.InvalidateVisual();
        _linealLinks.InvalidateVisual();
    }

    // ---------------------------------------------------------------- Zoom und Bildlauf

    public enum ZoomModus
    {
        GanzeSeite,
        Seitenbreite,
    }

    public void ZoomAnpassen(ZoomModus modus)
    {
        var dokument = Editor.Dokument;
        var breite = Bildlauf.ViewportWidth;
        var hoehe = Bildlauf.ViewportHeight;
        if (breite <= 0 || hoehe <= 0)
        {
            breite = Bildlauf.ActualWidth;
            hoehe = Bildlauf.ActualHeight;
        }
        if (breite <= 0 || hoehe <= 0)
            return;

        var dipProMm = 96.0 / 25.4;
        var verfuegbarBreite = Math.Max(50, breite - 80);
        var verfuegbarHoehe = Math.Max(50, hoehe - 60);
        var zoomBreite = verfuegbarBreite / (dokument.SeitenBreite * dipProMm);
        var zoomHoehe = verfuegbarHoehe / (dokument.SeitenHoehe * dipProMm);
        Editor.Zoom = modus == ZoomModus.GanzeSeite ? Math.Min(zoomBreite, zoomHoehe) : zoomBreite;
    }

    private void ZoomReglerGeaendert(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_intern || !IsLoaded)
            return;
        Editor.Zoom = e.NewValue / 100.0;
    }

    private void BildlaufRad(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            var faktor = e.Delta > 0 ? 1.1 : 1 / 1.1;
            var alt = Editor.Zoom;
            ZoomMitErhaltDerMitte(alt * faktor);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            Bildlauf.ScrollToHorizontalOffset(Bildlauf.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }

    /// <summary>Ändert den Zoom und hält dabei den Mittelpunkt der Ansicht an derselben Stelle des Dokuments.</summary>
    public void ZoomMitErhaltDerMitte(double neuerZoom)
    {
        var relX = Bildlauf.ExtentWidth > 0 ? (Bildlauf.HorizontalOffset + Bildlauf.ViewportWidth / 2) / Bildlauf.ExtentWidth : 0.5;
        var relY = Bildlauf.ExtentHeight > 0 ? (Bildlauf.VerticalOffset + Bildlauf.ViewportHeight / 2) / Bildlauf.ExtentHeight : 0.5;
        Editor.Zoom = neuerZoom;
        Bildlauf.UpdateLayout();
        Bildlauf.ScrollToHorizontalOffset(relX * Bildlauf.ExtentWidth - Bildlauf.ViewportWidth / 2);
        Bildlauf.ScrollToVerticalOffset(relY * Bildlauf.ExtentHeight - Bildlauf.ViewportHeight / 2);
    }

    private void BildlaufGeaendert(object sender, ScrollChangedEventArgs e)
    {
        AktualisiereLineale();
        if (e.VerticalChange == 0 || _ansichten.Count == 0 || Editor.Textbearbeitung != null)
            return;

        // Die Seite, die in der Mitte der Ansicht liegt, gilt als aktuelle Seite.
        var mitte = Bildlauf.ViewportHeight / 2;
        var beste = Editor.AktuelleSeite;
        var kleinsterAbstand = double.MaxValue;
        for (var i = 0; i < _ansichten.Count; i++)
        {
            if (!_ansichten[i].IsLoaded)
                continue;
            try
            {
                var oben = _ansichten[i].TransformToAncestor(Bildlauf).Transform(new Point(0, 0)).Y;
                var unten = oben + _ansichten[i].ActualHeight;
                var abstand = mitte < oben ? oben - mitte : mitte > unten ? mitte - unten : 0;
                if (abstand < kleinsterAbstand)
                {
                    kleinsterAbstand = abstand;
                    beste = i;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
        if (beste != Editor.AktuelleSeite)
        {
            _intern = true;
            Editor.AktuelleSeite = beste;
            _intern = false;
        }
    }

    // ---------------------------------------------------------------- Ansicht

    private void WendeAnsichtsoptionenAn()
    {
        LinealZeile.Height = new GridLength(Editor.ZeigeLineale ? Lineal.Dicke : 0);
        LinealSpalte.Width = new GridLength(Editor.ZeigeLineale ? Lineal.Dicke : 0);

        if (Editor.ZeigeNavigation)
        {
            if (_ausgeblendeteNavigation)
            {
                NavigationSpalte.Width = _navigationBreite;
                _ausgeblendeteNavigation = false;
            }
            SeitenListe.Visibility = Visibility.Visible;
            NavigationTeiler.Visibility = Visibility.Visible;
        }
        else
        {
            if (!_ausgeblendeteNavigation && NavigationSpalte.ActualWidth > 0)
                _navigationBreite = new GridLength(NavigationSpalte.ActualWidth);
            _ausgeblendeteNavigation = true;
            NavigationSpalte.Width = new GridLength(0);
            SeitenListe.Visibility = Visibility.Collapsed;
            NavigationTeiler.Visibility = Visibility.Collapsed;
        }
    }

    // ---------------------------------------------------------------- Titel und Statusleiste

    private void AktualisiereTitel()
    {
        var name = Editor.Pfad != null ? Path.GetFileName(Editor.Pfad) : Editor.Dokument.Titel ?? "Unbenannt";
        Title = $"{name}{(Editor.Geaendert ? " *" : "")} – Seitenwerk";
    }

    private void AktualisiereStatus()
    {
        StatusSeite.Text = $"Seite {Editor.AktuelleSeite + 1} von {Editor.Dokument.Seiten.Count}";

        var auswahl = Editor.Auswahl;
        if (auswahl.Count == 0)
        {
            StatusAuswahl.Text = "";
        }
        else if (auswahl.Count == 1)
        {
            var o = auswahl[0];
            var drehung = o.Drehung != 0 ? $"   Drehung: {o.Drehung:0.#}°" : "";
            StatusAuswahl.Text = string.Create(CultureInfo.CurrentCulture, $"Position: {o.X:0.0} / {o.Y:0.0} mm   Größe: {o.Breite:0.0} × {o.Hoehe:0.0} mm{drehung}");
        }
        else
        {
            StatusAuswahl.Text = $"{auswahl.Count} Objekte ausgewählt";
        }
    }

    public void ZeigeMeldung(string text)
    {
        StatusMeldung.Text = text;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (StatusMeldung.Text == text)
                StatusMeldung.Text = "";
        };
        timer.Start();
    }

    // ---------------------------------------------------------------- Tastatur

    private void FensterTaste(object sender, KeyEventArgs e)
    {
        var strg = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var umschalt = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var taste = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!strg)
            return;

        var textAktiv = Editor.Textbearbeitung != null;

        Action? aktion = null;
        switch (taste)
        {
            case Key.N: aktion = () => _befehle["Datei.Neu"](); break;
            case Key.O: aktion = () => _befehle["Datei.Oeffnen"](); break;
            case Key.S: aktion = () => _befehle[umschalt ? "Datei.SpeichernUnter" : "Datei.Speichern"](); break;
            case Key.P: aktion = () => _befehle["Datei.Drucken"](); break;
            case Key.OemPlus:
            case Key.Add: aktion = () => _befehle["Zoom.Plus"](); break;
            case Key.OemMinus:
            case Key.Subtract: aktion = () => _befehle["Zoom.Minus"](); break;
            case Key.D0:
            case Key.NumPad0: aktion = () => _befehle["Zoom.GanzeSeite"](); break;
        }

        if (aktion == null && !textAktiv)
        {
            switch (taste)
            {
                case Key.Z: aktion = () => _befehle["Bearbeiten.Rueckgaengig"](); break;
                case Key.Y: aktion = () => _befehle["Bearbeiten.Wiederholen"](); break;
                case Key.C: aktion = () => _befehle["Bearbeiten.Kopieren"](); break;
                case Key.X: aktion = () => _befehle["Bearbeiten.Ausschneiden"](); break;
                case Key.V: aktion = () => _befehle["Bearbeiten.Einfuegen"](); break;
                case Key.D: aktion = () => _befehle["Bearbeiten.Duplizieren"](); break;
                case Key.A: aktion = () => Editor.AllesAuswaehlen(); break;
                case Key.G: aktion = () => _befehle[umschalt ? "Anordnen.Aufheben" : "Anordnen.Gruppieren"](); break;
                case Key.B: aktion = () => ToggleFormat(z => z?.Fett, an => new TextFormat { Fett = an }); break;
                case Key.I: aktion = () => ToggleFormat(z => z?.Kursiv, an => new TextFormat { Kursiv = an }); break;
                case Key.U: aktion = () => ToggleFormat(z => z?.Unterstrichen, an => new TextFormat { Unterstrichen = an }); break;
            }
        }
        else if (aktion == null && textAktiv && taste == Key.Z && umschalt)
        {
            return;
        }

        if (aktion == null)
            return;
        e.Handled = true;
        Ausfuehren(aktion);
    }

    private void ToggleFormat(Func<TextFormatZustand?, bool?> lesen, Func<bool, TextFormat> format)
    {
        var an = lesen(Editor.LeseTextFormat()) != true;
        Editor.TextFormatAnwenden(format(an));
        AktualisiereTextFormat();
    }

    // ---------------------------------------------------------------- Drag & Drop

    private static readonly string[] BildEndungen = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".emf", ".wmf" };

    private void DateiUeberFenster(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DateiAbgelegt(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] dateien || dateien.Length == 0)
            return;

        var dokumentDatei = dateien.FirstOrDefault(d => new[] { ".swk", ".pub", ".odg" }.Contains(Path.GetExtension(d).ToLowerInvariant()));
        if (dokumentDatei != null)
        {
            _ = OeffneDateiAsync(dokumentDatei);
            return;
        }

        var seite = Editor.AktuelleSeiteModell;
        if (seite == null)
            return;
        Ausfuehren(() =>
        {
            foreach (var datei in dateien.Where(d => BildEndungen.Contains(Path.GetExtension(d).ToLowerInvariant())))
                Editor.BildEinfuegen(File.ReadAllBytes(datei), seite);
        });
    }
}
