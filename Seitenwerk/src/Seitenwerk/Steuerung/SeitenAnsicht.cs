using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Seitenwerk.Core.Modell;
using Seitenwerk.Darstellung;

namespace Seitenwerk.Steuerung;

public enum Griff
{
    Keiner,
    NordWest,
    Nord,
    NordOst,
    Ost,
    SuedOst,
    Sued,
    SuedWest,
    West,
    Drehen,
    LinieAnfang,
    LinieEnde,
}

internal sealed record VorschauForm(bool Linie, double X1, double Y1, double X2, double Y2);

/// <summary>Eine Seite des Dokuments im Bearbeitungsfenster: Anzeige, Auswahl, Verschieben, Skalieren, Drehen, Zeichnen und Textbearbeitung.</summary>
public sealed partial class SeitenAnsicht : Grid
{
    public const double RasterAbstand = 10;
    private const double MindestGroesse = 2;

    private readonly SeitenZeichnung _zeichnung;
    private readonly AuswahlZeichnung _ueberlagerung;
    private readonly Canvas _textEbene = new();
    private RichTextBearbeitung? _textBearbeitung;

    public SeitenAnsicht(EditorSitzung editor, int index)
    {
        Editor = editor;
        Index = index;
        Focusable = true;
        FocusVisualStyle = null;
        Background = Brushes.Transparent; // damit leere Flächen Mausereignisse erhalten
        ClipToBounds = false;
        Cursor = Cursors.Arrow;
        UseLayoutRounding = false;

        _zeichnung = new SeitenZeichnung(this);
        _ueberlagerung = new AuswahlZeichnung(this);
        Children.Add(_zeichnung);
        Children.Add(_ueberlagerung);
        Children.Add(_textEbene);

        MouseLeftButtonDown += MausLinksUnten;
        MouseMove += MausBewegt;
        MouseLeftButtonUp += MausLinksOben;
        MouseRightButtonDown += MausRechtsUnten;
        MouseLeave += (_, _) => SetzeUeberfahren(null);
        LostMouseCapture += (_, _) =>
        {
            if (_zustand != Zustand.Leer)
                Abbrechen();
        };
        KeyDown += TasteGedrueckt;
        ContextMenuOpening += (_, e) => e.Handled = !ZeigeKontextmenue();

        Aktualisieren();
    }

    public EditorSitzung Editor { get; }
    public int Index { get; set; }

    public Dokument Dokument => Editor.Dokument;
    public Seite? Seite => Index >= 0 && Index < Dokument.Seiten.Count ? Dokument.Seiten[Index] : null;

    /// <summary>Bildschirmeinheiten (DIP) pro Millimeter.</summary>
    public double Skala => Editor.Zoom * 96.0 / 25.4;

    public string? BearbeiteteObjektId => _textBearbeitung?.Objekt.Id;
    public bool TextWirdBearbeitet => _textBearbeitung != null;
    public Objekt? Ueberfahren { get; private set; }

    /// <summary>Ausgewählte Objekte dieser Seite.</summary>
    public IReadOnlyList<Objekt> Auswahl
    {
        get
        {
            var seite = Seite;
            if (seite == null)
                return Array.Empty<Objekt>();
            return Editor.Auswahl.Where(o => seite.Objekte.Contains(o)).ToList();
        }
    }

    internal List<(bool Senkrecht, double Position)> Fanglinien { get; } = new();
    internal Rect? Gummiband { get; private set; }
    internal VorschauForm? Vorschau { get; private set; }

    /// <summary>Passt Größe und Anzeige an Zoom, Seitengröße und Inhalt an.</summary>
    public void Aktualisieren()
    {
        var neueBreite = Dokument.SeitenBreite * Skala;
        var neueHoehe = Dokument.SeitenHoehe * Skala;
        if (Math.Abs(Width - neueBreite) > 0.01 || Math.Abs(Height - neueHoehe) > 0.01 || double.IsNaN(Width))
        {
            // Bei geändertem Maßstab oder geänderter Seitengröße muss eine laufende Textbearbeitung enden.
            _textBearbeitung?.Beenden(true);
            Width = neueBreite;
            Height = neueHoehe;
        }
        Neuzeichnen();
    }

    public void Neuzeichnen()
    {
        _zeichnung.InvalidateVisual();
        _ueberlagerung.InvalidateVisual();
    }

    public void ZeichneUeberlagerungNeu() => _ueberlagerung.InvalidateVisual();

    // ---------------------------------------------------------------- Umrechnung

    private Point MmPunkt(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        return new Point(p.X / Skala, p.Y / Skala);
    }

    private double Toleranz => 4 / Skala;

    private Objekt? Treffer(double x, double y)
    {
        var seite = Seite;
        if (seite == null)
            return null;
        for (var i = seite.Objekte.Count - 1; i >= 0; i--)
        {
            if (Geometrie.Trifft(seite.Objekte[i], x, y, Toleranz))
                return seite.Objekte[i];
        }
        return null;
    }

    // ---------------------------------------------------------------- Griffe

    public static bool KannDrehen(Objekt objekt) => objekt is not (GruppeObjekt or LinieObjekt);

    internal IEnumerable<(Griff Griff, Point Mm)> Griffe(Objekt o)
    {
        if (o is LinieObjekt linie)
        {
            var (x1, y1, x2, y2) = linie.Punkte;
            yield return (Griff.LinieAnfang, new Point(x1, y1));
            yield return (Griff.LinieEnde, new Point(x2, y2));
            yield break;
        }

        Point Dreh(double x, double y)
        {
            var (rx, ry) = Geometrie.Drehen(x, y, o.MitteX, o.MitteY, o.Drehung);
            return new Point(rx, ry);
        }

        var links = o.X;
        var rechts = o.X + o.Breite;
        var oben = o.Y;
        var unten = o.Y + o.Hoehe;
        yield return (Griff.NordWest, Dreh(links, oben));
        yield return (Griff.Nord, Dreh(o.MitteX, oben));
        yield return (Griff.NordOst, Dreh(rechts, oben));
        yield return (Griff.Ost, Dreh(rechts, o.MitteY));
        yield return (Griff.SuedOst, Dreh(rechts, unten));
        yield return (Griff.Sued, Dreh(o.MitteX, unten));
        yield return (Griff.SuedWest, Dreh(links, unten));
        yield return (Griff.West, Dreh(links, o.MitteY));
    }

    internal (double RotX, double RotY, double AnkerX, double AnkerY) DrehGriff(Objekt o)
    {
        var abstand = AuswahlZeichnung.DrehAbstand / Skala;
        var (rx, ry) = Geometrie.Drehen(o.MitteX, o.Y - abstand, o.MitteX, o.MitteY, o.Drehung);
        var (ax, ay) = Geometrie.Drehen(o.MitteX, o.Y, o.MitteX, o.MitteY, o.Drehung);
        return (rx, ry, ax, ay);
    }

    private Griff GriffAn(Point mm)
    {
        var auswahl = Auswahl;
        if (auswahl.Count != 1 || auswahl[0].Gesperrt || TextWirdBearbeitet)
            return Griff.Keiner;
        var objekt = auswahl[0];
        var grenze = (AuswahlZeichnung.GriffGroesse / 2 + 3) / Skala;

        if (KannDrehen(objekt))
        {
            var (rx, ry, _, _) = DrehGriff(objekt);
            if (Math.Abs(mm.X - rx) <= 7 / Skala && Math.Abs(mm.Y - ry) <= 7 / Skala)
                return Griff.Drehen;
        }

        foreach (var (griff, punkt) in Griffe(objekt))
        {
            if (Math.Abs(mm.X - punkt.X) <= grenze && Math.Abs(mm.Y - punkt.Y) <= grenze)
                return griff;
        }
        return Griff.Keiner;
    }

    private static Cursor CursorFuer(Griff griff, Objekt objekt)
    {
        if (griff == Griff.Drehen)
            return Cursors.Cross;
        if (griff is Griff.LinieAnfang or Griff.LinieEnde)
            return Cursors.Cross;
        // Der Cursor richtet sich nach der Lage des Griffs; bei gedrehten Objekten wird die Richtung grob mitgedreht.
        var index = griff switch
        {
            Griff.Nord => 0,
            Griff.NordOst => 1,
            Griff.Ost => 2,
            Griff.SuedOst => 3,
            Griff.Sued => 4,
            Griff.SuedWest => 5,
            Griff.West => 6,
            Griff.NordWest => 7,
            _ => 0,
        };
        index = ((index + (int)Math.Round(objekt.Drehung / 45.0)) % 8 + 8) % 8;
        return (index % 4) switch
        {
            0 => Cursors.SizeNS,
            1 => Cursors.SizeNESW,
            2 => Cursors.SizeWE,
            _ => Cursors.SizeNWSE,
        };
    }

    private void SetzeUeberfahren(Objekt? objekt)
    {
        if (ReferenceEquals(Ueberfahren, objekt))
            return;
        Ueberfahren = objekt;
        _ueberlagerung.InvalidateVisual();
    }

    // ---------------------------------------------------------------- Kontextmenü

    private bool ZeigeKontextmenue()
    {
        var menue = new ContextMenu();
        void Eintrag(string text, Action aktion, bool aktiv = true, string? kurz = null)
        {
            var item = new MenuItem { Header = text, IsEnabled = aktiv, InputGestureText = kurz };
            item.Click += (_, _) => aktion();
            menue.Items.Add(item);
        }

        var hatAuswahl = Editor.Auswahl.Count > 0;
        if (hatAuswahl)
        {
            if (Editor.Auswahl.Count == 1 && Editor.Auswahl[0] is TextTraeger t)
                Eintrag("Text bearbeiten", () => BeginneTextBearbeitung(t), !t.Gesperrt, "F2");
            Eintrag("Ausschneiden", Editor.Ausschneiden, true, "Strg+X");
            Eintrag("Kopieren", Editor.Kopieren, true, "Strg+C");
        }
        Eintrag("Einfügen", Editor.Einfuegen, Editor.KannEinfuegen(), "Strg+V");
        if (hatAuswahl)
        {
            Eintrag("Duplizieren", Editor.AuswahlDuplizieren, true, "Strg+D");
            Eintrag("Löschen", Editor.AuswahlLoeschen, true, "Entf");
            menue.Items.Add(new Separator());
            Eintrag("In den Vordergrund", Editor.InDenVordergrund);
            Eintrag("In den Hintergrund", Editor.InDenHintergrund);
            Eintrag("Eine Ebene nach vorn", Editor.EineEbeneNachVorn);
            Eintrag("Eine Ebene nach hinten", Editor.EineEbeneNachHinten);
            menue.Items.Add(new Separator());
            Eintrag("Gruppieren", Editor.Gruppieren, Editor.Auswahl.Count > 1, "Strg+G");
            Eintrag("Gruppierung aufheben", Editor.GruppierungAufheben, Editor.Auswahl.Any(o => o is GruppeObjekt), "Strg+Umschalt+G");
            Eintrag(Editor.Auswahl.Any(o => !o.Gesperrt) ? "Sperren" : "Entsperren", Editor.SperreUmschalten);
        }
        ContextMenu = menue;
        return true;
    }
}
