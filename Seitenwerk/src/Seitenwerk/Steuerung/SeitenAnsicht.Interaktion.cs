using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Seitenwerk.Core.Bearbeiten;
using Seitenwerk.Core.Modell;
using Seitenwerk.Darstellung;

namespace Seitenwerk.Steuerung;

public sealed partial class SeitenAnsicht
{
    private enum Zustand
    {
        Leer,
        Verschieben,
        Skalieren,
        Drehen,
        LinienEnde,
        Zeichnen,
        Gummiband,
    }

    private Zustand _zustand;
    private Point _startMm;
    private Point _startPx;
    private bool _bewegt;
    private bool _mitModifikator;
    private Griff _griff;
    private Objekt? _bearbeitetes;
    private Objekt? _klickObjekt;
    private (double X, double Y, double B, double H, double Drehung) _startLage;
    private (double X1, double Y1, double X2, double Y2) _startLinie;
    private List<Objekt> _bewegte = new();
    private Vector _angewendet;
    private (double X0, double Y0, double X1, double Y1) _startBox;

    private static bool Umschalt => (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
    private static bool Strg => (Keyboard.Modifiers & ModifierKeys.Control) != 0;

    // ---------------------------------------------------------------- Maus

    private bool IstInTextEbene(DependencyObject? quelle)
    {
        for (var e = quelle; e != null; e = VisualTreeHelper.GetParent(e) ?? LogicalTreeHelper.GetParent(e))
        {
            if (ReferenceEquals(e, _textEbene))
                return true;
        }
        return false;
    }

    private void MausLinksUnten(object sender, MouseButtonEventArgs e)
    {
        if (IstInTextEbene(e.OriginalSource as DependencyObject))
            return;

        _textBearbeitung?.Beenden(true);
        Focus();
        Editor.AktuelleSeite = Index;

        var mm = MmPunkt(e);
        _startMm = mm;
        _startPx = e.GetPosition(this);
        _bewegt = false;
        _mitModifikator = Umschalt || Strg;
        _klickObjekt = null;
        Fanglinien.Clear();

        if (Editor.AktivesWerkzeug != Werkzeug.Auswahl)
        {
            _zustand = Zustand.Zeichnen;
            _startMm = FangePunkt(mm, Array.Empty<Objekt>());
            Fanglinien.Clear();
            CaptureMouse();
            e.Handled = true;
            return;
        }

        var griff = GriffAn(mm);
        if (griff != Griff.Keiner)
        {
            BeginneGriff(griff, Auswahl[0]);
            CaptureMouse();
            e.Handled = true;
            return;
        }

        var treffer = Treffer(mm.X, mm.Y);
        if (treffer != null)
        {
            _klickObjekt = treffer;
            if (e.ClickCount == 2 && treffer is TextTraeger text && !text.Gesperrt && !_mitModifikator)
            {
                Editor.AuswahlSetzen(new[] { treffer });
                BeginneTextBearbeitung(text);
                e.Handled = true;
                return;
            }

            if (_mitModifikator)
                Editor.AuswahlUmschalten(treffer);
            else if (!Editor.Auswahl.Contains(treffer))
                Editor.AuswahlSetzen(new[] { treffer });

            if (Editor.Auswahl.Contains(treffer) && !treffer.Gesperrt)
            {
                _zustand = Zustand.Verschieben;
                _bewegte = Auswahl.Where(o => !o.Gesperrt).ToList();
                _angewendet = new Vector();
                _startBox = Geometrie.UmgebendesRechteck(_bewegte);
                CaptureMouse();
            }
        }
        else
        {
            if (!_mitModifikator)
                Editor.AuswahlLeeren();
            _zustand = Zustand.Gummiband;
            CaptureMouse();
        }
        e.Handled = true;
    }

    private void MausRechtsUnten(object sender, MouseButtonEventArgs e)
    {
        if (IstInTextEbene(e.OriginalSource as DependencyObject))
            return;
        Focus();
        Editor.AktuelleSeite = Index;
        var mm = MmPunkt(e);
        var treffer = Treffer(mm.X, mm.Y);
        if (treffer == null)
            Editor.AuswahlLeeren();
        else if (!Editor.Auswahl.Contains(treffer))
            Editor.AuswahlSetzen(new[] { treffer });
    }

    private void BeginneGriff(Griff griff, Objekt objekt)
    {
        _griff = griff;
        _bearbeitetes = objekt;
        _startLage = (objekt.X, objekt.Y, objekt.Breite, objekt.Hoehe, objekt.Drehung);
        if (objekt is LinieObjekt linie)
            _startLinie = linie.Punkte;
        _zustand = griff switch
        {
            Griff.Drehen => Zustand.Drehen,
            Griff.LinieAnfang or Griff.LinieEnde => Zustand.LinienEnde,
            _ => Zustand.Skalieren,
        };
    }

    private void MausBewegt(object sender, MouseEventArgs e)
    {
        var mm = MmPunkt(e);
        var px = e.GetPosition(this);

        if (_zustand == Zustand.Leer)
        {
            AktualisiereCursor(mm);
            return;
        }

        if (!_bewegt && Math.Abs(px.X - _startPx.X) < 3 && Math.Abs(px.Y - _startPx.Y) < 3)
            return;
        _bewegt = true;
        Fanglinien.Clear();

        switch (_zustand)
        {
            case Zustand.Verschieben:
                Verschiebe(mm);
                break;
            case Zustand.Skalieren:
                if (_bearbeitetes is GruppeObjekt gruppe)
                    SkaliereGruppe(gruppe, mm);
                else if (_bearbeitetes != null)
                    SkaliereEinzel(_bearbeitetes, mm);
                break;
            case Zustand.Drehen:
                Drehe(mm);
                break;
            case Zustand.LinienEnde:
                BewegeLinienEnde(mm);
                break;
            case Zustand.Zeichnen:
                AktualisiereVorschau(mm);
                break;
            case Zustand.Gummiband:
                Gummiband = new Rect(_startMm, mm);
                break;
        }

        _ueberlagerung.InvalidateVisual();
        if (_zustand is Zustand.Verschieben or Zustand.Skalieren or Zustand.Drehen or Zustand.LinienEnde)
        {
            _zeichnung.InvalidateVisual();
            Editor.MeldeLage();
        }
    }

    private void AktualisiereCursor(Point mm)
    {
        if (Editor.AktivesWerkzeug != Werkzeug.Auswahl)
        {
            Cursor = Cursors.Cross;
            SetzeUeberfahren(null);
            return;
        }

        var griff = GriffAn(mm);
        if (griff != Griff.Keiner)
        {
            Cursor = CursorFuer(griff, Auswahl[0]);
            SetzeUeberfahren(null);
            return;
        }

        var treffer = Treffer(mm.X, mm.Y);
        SetzeUeberfahren(treffer);
        Cursor = treffer == null || treffer.Gesperrt ? Cursors.Arrow : Cursors.SizeAll;
    }

    private void MausLinksOben(object sender, MouseButtonEventArgs e)
    {
        if (_zustand == Zustand.Leer)
            return;

        var zustand = _zustand;
        var bewegt = _bewegt;
        var mm = MmPunkt(e);
        _zustand = Zustand.Leer;
        ReleaseMouseCapture();
        Fanglinien.Clear();

        switch (zustand)
        {
            case Zustand.Verschieben:
                if (bewegt)
                    Editor.Abschliessen();
                else if (!_mitModifikator && Editor.Auswahl.Count > 1 && _klickObjekt != null)
                    Editor.AuswahlSetzen(new[] { _klickObjekt }); // Klick ohne Ziehen in einer Mehrfachauswahl wählt nur dieses Objekt
                break;
            case Zustand.Skalieren:
            case Zustand.Drehen:
            case Zustand.LinienEnde:
                if (bewegt)
                    Editor.Abschliessen();
                break;
            case Zustand.Zeichnen:
                ErzeugeObjekt(mm, bewegt);
                break;
            case Zustand.Gummiband:
                WaehleImBand(bewegt);
                break;
        }

        Gummiband = null;
        Vorschau = null;
        Neuzeichnen();
        AktualisiereCursor(mm);
    }

    private void Abbrechen()
    {
        var zustand = _zustand;
        _zustand = Zustand.Leer;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        Fanglinien.Clear();
        Gummiband = null;
        Vorschau = null;
        if (zustand is Zustand.Verschieben or Zustand.Skalieren or Zustand.Drehen or Zustand.LinienEnde && _bewegt)
            Editor.LetztenStandWiederherstellen();
        else
            Neuzeichnen();
    }

    private void WaehleImBand(bool bewegt)
    {
        var seite = Seite;
        if (seite == null || !bewegt || Gummiband is not { } band)
            return;
        var treffer = seite.Objekte
            .Where(o => !o.Gesperrt)
            .Where(o =>
            {
                var (x0, y0, x1, y1) = Geometrie.UmgebendesRechteck(o);
                return x0 >= band.Left && y0 >= band.Top && x1 <= band.Right && y1 <= band.Bottom;
            })
            .ToList();
        if (_mitModifikator)
            treffer = Editor.Auswahl.Where(o => seite.Objekte.Contains(o)).Union(treffer).ToList();
        Editor.AuswahlSetzen(treffer);
    }

    // ---------------------------------------------------------------- Fangen

    private (List<double> Xs, List<double> Ys) Ziele(IReadOnlyCollection<Objekt> ausser)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        if (!Editor.ObjektFang)
            return (xs, ys);

        var d = Dokument;
        xs.AddRange(new[] { 0, d.SeitenBreite / 2, d.SeitenBreite, d.Raender.Links, d.SeitenBreite - d.Raender.Rechts });
        ys.AddRange(new[] { 0, d.SeitenHoehe / 2, d.SeitenHoehe, d.Raender.Oben, d.SeitenHoehe - d.Raender.Unten });

        var seite = Seite;
        if (seite != null)
        {
            foreach (var o in seite.Objekte)
            {
                if (ausser.Contains(o))
                    continue;
                var (x0, y0, x1, y1) = Geometrie.UmgebendesRechteck(o);
                xs.AddRange(new[] { x0, (x0 + x1) / 2, x1 });
                ys.AddRange(new[] { y0, (y0 + y1) / 2, y1 });
            }
        }
        return (xs, ys);
    }

    private (double Delta, double Ziel)? Naechster(IEnumerable<double> kandidaten, List<double> ziele)
    {
        var schwelle = 5 / Skala;
        (double Delta, double Ziel)? beste = null;
        foreach (var k in kandidaten)
        {
            foreach (var z in ziele)
            {
                var delta = z - k;
                if (Math.Abs(delta) <= schwelle && (beste == null || Math.Abs(delta) < Math.Abs(beste.Value.Delta)))
                    beste = (delta, z);
            }
            if (Editor.RasterEinrasten)
            {
                var gerastert = Math.Round(k / RasterAbstand) * RasterAbstand;
                var delta = gerastert - k;
                if (Math.Abs(delta) <= schwelle && (beste == null || Math.Abs(delta) < Math.Abs(beste.Value.Delta)))
                    beste = (delta, gerastert);
            }
        }
        return beste;
    }

    private Point FangePunkt(Point mm, IReadOnlyCollection<Objekt> ausser)
    {
        if (!Editor.ObjektFang && !Editor.RasterEinrasten)
            return mm;
        var (xs, ys) = Ziele(ausser);
        var x = Naechster(new[] { mm.X }, xs);
        var y = Naechster(new[] { mm.Y }, ys);
        if (x != null)
        {
            mm.X = x.Value.Ziel;
            Fanglinien.Add((true, x.Value.Ziel));
        }
        if (y != null)
        {
            mm.Y = y.Value.Ziel;
            Fanglinien.Add((false, y.Value.Ziel));
        }
        return mm;
    }

    // ---------------------------------------------------------------- Verschieben

    private void Verschiebe(Point mm)
    {
        var dx = mm.X - _startMm.X;
        var dy = mm.Y - _startMm.Y;
        if (Umschalt && !_mitModifikator)
        {
            if (Math.Abs(dx) > Math.Abs(dy))
                dy = 0;
            else
                dx = 0;
        }

        if (Editor.ObjektFang || Editor.RasterEinrasten)
        {
            var (xs, ys) = Ziele(_bewegte);
            var x0 = _startBox.X0 + dx;
            var x1 = _startBox.X1 + dx;
            var y0 = _startBox.Y0 + dy;
            var y1 = _startBox.Y1 + dy;
            var fangX = Naechster(new[] { x0, (x0 + x1) / 2, x1 }, xs);
            var fangY = Naechster(new[] { y0, (y0 + y1) / 2, y1 }, ys);
            if (fangX != null)
            {
                dx += fangX.Value.Delta;
                Fanglinien.Add((true, fangX.Value.Ziel));
            }
            if (fangY != null)
            {
                dy += fangY.Value.Delta;
                Fanglinien.Add((false, fangY.Value.Ziel));
            }
        }

        var schritt = new Vector(dx, dy) - _angewendet;
        foreach (var o in _bewegte)
            o.Verschieben(schritt.X, schritt.Y);
        _angewendet = new Vector(dx, dy);
    }

    // ---------------------------------------------------------------- Skalieren

    private static (bool West, bool Ost, bool Nord, bool Sued) Seiten(Griff g) => (
        g is Griff.West or Griff.NordWest or Griff.SuedWest,
        g is Griff.Ost or Griff.NordOst or Griff.SuedOst,
        g is Griff.Nord or Griff.NordWest or Griff.NordOst,
        g is Griff.Sued or Griff.SuedWest or Griff.SuedOst);

    private void SkaliereEinzel(Objekt o, Point maus)
    {
        var (westen, osten, norden, sueden) = Seiten(_griff);
        var (sx, sy, sb, sh, winkel) = _startLage;
        var cx0 = sx + sb / 2;
        var cy0 = sy + sh / 2;

        // Fangen nur bei ungedrehten Objekten, weil die Fangpunkte achsenparallel sind.
        if (winkel == 0)
        {
            var (xs, ys) = Ziele(new[] { o });
            if (westen || osten)
            {
                if (Naechster(new[] { maus.X }, xs) is { } fx)
                {
                    maus.X = fx.Ziel;
                    Fanglinien.Add((true, fx.Ziel));
                }
            }
            if (norden || sueden)
            {
                if (Naechster(new[] { maus.Y }, ys) is { } fy)
                {
                    maus.Y = fy.Ziel;
                    Fanglinien.Add((false, fy.Ziel));
                }
            }
        }

        var (mx, my) = Geometrie.Drehen(maus.X, maus.Y, cx0, cy0, -winkel);
        mx -= cx0;
        my -= cy0;

        double links = -sb / 2, rechts = sb / 2, oben = -sh / 2, unten = sh / 2;
        if (osten) rechts = Math.Max(mx, links + MindestGroesse);
        if (westen) links = Math.Min(mx, rechts - MindestGroesse);
        if (sueden) unten = Math.Max(my, oben + MindestGroesse);
        if (norden) oben = Math.Min(my, unten - MindestGroesse);

        var ecke = (westen || osten) && (norden || sueden);
        var behalteVerhaeltnis = ecke && (o is BildObjekt ? !Umschalt : Umschalt) && sb > 0 && sh > 0;
        if (behalteVerhaeltnis)
        {
            var fb = (rechts - links) / sb;
            var fh = (unten - oben) / sh;
            var faktor = Math.Abs(fb - 1) > Math.Abs(fh - 1) ? fb : fh;
            faktor = Math.Max(faktor, MindestGroesse / Math.Min(sb, sh));
            var neuB = sb * faktor;
            var neuH = sh * faktor;
            if (osten) rechts = links + neuB; else links = rechts - neuB;
            if (sueden) unten = oben + neuH; else oben = unten - neuH;
        }

        var breite = rechts - links;
        var hoehe = unten - oben;
        var (wx, wy) = Geometrie.Drehen(cx0 + (links + rechts) / 2, cy0 + (oben + unten) / 2, cx0, cy0, winkel);
        o.Breite = breite;
        o.Hoehe = hoehe;
        o.X = wx - breite / 2;
        o.Y = wy - hoehe / 2;
    }

    private void SkaliereGruppe(GruppeObjekt gruppe, Point maus)
    {
        var (westen, osten, norden, sueden) = Seiten(_griff);
        var (sx, sy, sb, sh, _) = _startLage;

        double links = sx, rechts = sx + sb, oben = sy, unten = sy + sh;
        if (osten) rechts = Math.Max(maus.X, links + MindestGroesse);
        if (westen) links = Math.Min(maus.X, rechts - MindestGroesse);
        if (sueden) unten = Math.Max(maus.Y, oben + MindestGroesse);
        if (norden) oben = Math.Min(maus.Y, unten - MindestGroesse);

        var ecke = (westen || osten) && (norden || sueden);
        if (ecke && Umschalt && sb > 0 && sh > 0)
        {
            var fb = (rechts - links) / sb;
            var fh = (unten - oben) / sh;
            var faktor = Math.Abs(fb - 1) > Math.Abs(fh - 1) ? fb : fh;
            if (osten) rechts = links + sb * faktor; else links = rechts - sb * faktor;
            if (sueden) unten = oben + sh * faktor; else oben = unten - sh * faktor;
        }

        var faktorX = gruppe.Breite > 0 ? (rechts - links) / gruppe.Breite : 1;
        var faktorY = gruppe.Hoehe > 0 ? (unten - oben) / gruppe.Hoehe : 1;
        gruppe.Skalieren(faktorX, faktorY, westen ? sx + sb : sx, norden ? sy + sh : sy);
        // Fixpunkt ist die gegenüberliegende Kante; bei reinen Seitengriffen bleibt die andere Achse unverändert.
        gruppe.GrenzenNeuBerechnen();
    }

    // ---------------------------------------------------------------- Drehen und Linien

    private void Drehe(Point maus)
    {
        if (_bearbeitetes == null)
            return;
        var o = _bearbeitetes;
        var winkel = Math.Atan2(maus.Y - o.MitteY, maus.X - o.MitteX) * 180 / Math.PI + 90;
        winkel = Anordnung.Normieren(winkel);
        if (Umschalt)
        {
            winkel = Math.Round(winkel / 15) * 15;
        }
        else
        {
            foreach (var raste in new[] { 0.0, 90, 180, 270, 360 })
            {
                if (Math.Abs(winkel - raste) < 3)
                    winkel = raste;
            }
        }
        o.Drehung = Anordnung.Normieren(winkel);
    }

    private void BewegeLinienEnde(Point maus)
    {
        if (_bearbeitetes is not LinieObjekt linie)
            return;
        var (x1, y1, x2, y2) = _startLinie;
        var festX = _griff == Griff.LinieAnfang ? x2 : x1;
        var festY = _griff == Griff.LinieAnfang ? y2 : y1;

        var ziel = FangePunkt(maus, new[] { (Objekt)linie });
        if (Umschalt)
            ziel = Winkelraster(festX, festY, maus);

        if (_griff == Griff.LinieAnfang)
        {
            x1 = ziel.X;
            y1 = ziel.Y;
        }
        else
        {
            x2 = ziel.X;
            y2 = ziel.Y;
        }

        var neu = LinieObjekt.Zwischen(x1, y1, x2, y2);
        linie.X = neu.X;
        linie.Y = neu.Y;
        linie.Breite = neu.Breite;
        linie.Hoehe = neu.Hoehe;
        linie.Aufsteigend = neu.Aufsteigend;
        linie.AnfangLinks = neu.AnfangLinks;
    }

    /// <summary>Begrenzt eine Linie auf Vielfache von 45°.</summary>
    private static Point Winkelraster(double vonX, double vonY, Point nach)
    {
        var dx = nach.X - vonX;
        var dy = nach.Y - vonY;
        var laenge = Math.Sqrt(dx * dx + dy * dy);
        if (laenge < 1e-6)
            return nach;
        var winkel = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
        return new Point(vonX + Math.Cos(winkel) * laenge, vonY + Math.Sin(winkel) * laenge);
    }

    // ---------------------------------------------------------------- Zeichnen

    private void AktualisiereVorschau(Point mm)
    {
        var ende = FangePunkt(mm, Array.Empty<Objekt>());
        var werkzeug = Editor.AktivesWerkzeug;
        if (werkzeug is Werkzeug.Linie or Werkzeug.Pfeil)
        {
            if (Umschalt)
                ende = Winkelraster(_startMm.X, _startMm.Y, ende);
            Vorschau = new VorschauForm(true, _startMm.X, _startMm.Y, ende.X, ende.Y);
        }
        else
        {
            ende = QuadratWennNoetig(ende);
            Vorschau = new VorschauForm(false, _startMm.X, _startMm.Y, ende.X, ende.Y);
        }
    }

    private Point QuadratWennNoetig(Point ende)
    {
        if (!Umschalt)
            return ende;
        var dx = ende.X - _startMm.X;
        var dy = ende.Y - _startMm.Y;
        var seite = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return new Point(_startMm.X + Math.Sign(dx == 0 ? 1 : dx) * seite, _startMm.Y + Math.Sign(dy == 0 ? 1 : dy) * seite);
    }

    private void ErzeugeObjekt(Point mm, bool bewegt)
    {
        var seite = Seite;
        var werkzeug = Editor.AktivesWerkzeug;
        if (seite == null)
            return;

        var ende = FangePunkt(mm, Array.Empty<Objekt>());
        Fanglinien.Clear();

        if (werkzeug is Werkzeug.Linie or Werkzeug.Pfeil)
        {
            if (Umschalt)
                ende = Winkelraster(_startMm.X, _startMm.Y, ende);
            var laenge = (ende - _startMm).Length;
            if (!bewegt || laenge < 2)
            {
                Editor.AktivesWerkzeug = Werkzeug.Auswahl;
                return;
            }
            var linie = LinieObjekt.Zwischen(_startMm.X, _startMm.Y, ende.X, ende.Y);
            if (werkzeug == Werkzeug.Pfeil)
                linie.Ende = Pfeilspitze.Pfeil;
            Editor.AktivesWerkzeug = Werkzeug.Auswahl;
            Editor.ObjektHinzufuegen(seite, linie);
            return;
        }

        var istText = werkzeug == Werkzeug.Textfeld;
        ende = QuadratWennNoetig(ende);
        var rechteck = new Rect(_startMm, ende);
        if (!bewegt || rechteck.Width < 5 || rechteck.Height < 5)
        {
            var b = istText ? 60.0 : 40.0;
            var h = istText ? 25.0 : 30.0;
            rechteck = new Rect(_startMm.X, _startMm.Y, b, h);
        }
        // Auf der Seite bleiben
        rechteck.X = Math.Clamp(rechteck.X, 0, Math.Max(0, Dokument.SeitenBreite - rechteck.Width));
        rechteck.Y = Math.Clamp(rechteck.Y, 0, Math.Max(0, Dokument.SeitenHoehe - rechteck.Height));

        Editor.AktivesWerkzeug = Werkzeug.Auswahl;
        if (istText)
        {
            var feld = TextfeldObjekt.Neu(Math.Round(rechteck.X, 2), Math.Round(rechteck.Y, 2), Math.Round(rechteck.Width, 2), Math.Round(rechteck.Height, 2));
            Editor.ObjektHinzufuegen(seite, feld);
            BeginneTextBearbeitung(feld);
        }
        else
        {
            var form = new FormObjekt
            {
                Form = Editor.NeueForm,
                X = Math.Round(rechteck.X, 2),
                Y = Math.Round(rechteck.Y, 2),
                Breite = Math.Round(rechteck.Width, 2),
                Hoehe = Math.Round(rechteck.Height, 2),
            };
            Editor.ObjektHinzufuegen(seite, form);
        }
    }

    // ---------------------------------------------------------------- Tastatur

    private void TasteGedrueckt(object sender, KeyEventArgs e)
    {
        if (TextWirdBearbeitet)
            return;

        switch (e.Key)
        {
            case Key.Delete:
            case Key.Back:
                if (Editor.Auswahl.Count > 0)
                {
                    Editor.AuswahlLoeschen();
                    e.Handled = true;
                }
                break;
            case Key.Escape:
                if (_zustand != Zustand.Leer)
                    Abbrechen();
                else if (Editor.AktivesWerkzeug != Werkzeug.Auswahl)
                    Editor.AktivesWerkzeug = Werkzeug.Auswahl;
                else
                    Editor.AuswahlLeeren();
                e.Handled = true;
                break;
            case Key.F2:
            case Key.Enter:
                if (Editor.Auswahl.Count == 1 && Editor.Auswahl[0] is TextTraeger text && !text.Gesperrt && Auswahl.Count == 1)
                {
                    BeginneTextBearbeitung(text);
                    e.Handled = true;
                }
                break;
            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
                if (Editor.Auswahl.Count > 0)
                {
                    var schritt = Strg ? 0.1 : Umschalt ? 10.0 : 1.0;
                    var dx = e.Key == Key.Left ? -schritt : e.Key == Key.Right ? schritt : 0;
                    var dy = e.Key == Key.Up ? -schritt : e.Key == Key.Down ? schritt : 0;
                    Editor.AuswahlVerschieben(dx, dy);
                    e.Handled = true;
                }
                break;
        }
    }

    // ---------------------------------------------------------------- Textbearbeitung

    public void BeginneTextBearbeitung(TextTraeger traeger)
    {
        var seite = Seite;
        if (seite == null || !seite.Objekte.Contains(traeger))
            return;
        _textBearbeitung?.Beenden(true);

        const double dip = 96.0 / 25.4;
        var rand = Math.Min(traeger.Innenabstand, Math.Min(traeger.Breite, traeger.Hoehe) / 2.5);
        var ix = traeger.X + rand;
        var iy = traeger.Y + rand;
        var breite = Math.Max(1, traeger.Breite - 2 * rand);
        var hoehe = Math.Max(1, traeger.Hoehe - 2 * rand);

        var box = new RichTextBox
        {
            Document = TextKonverter.ZuFlowDocument(traeger.Absaetze),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            AcceptsReturn = true,
            AcceptsTab = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Width = breite * dip,
            VerticalAlignment = traeger.VertikaleAusrichtung switch
            {
                VertikaleAusrichtung.Mitte => VerticalAlignment.Center,
                VertikaleAusrichtung.Unten => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Top,
            },
            Language = XmlLanguage.GetLanguage("de-DE"),
            IsInactiveSelectionHighlightEnabled = true,
            FocusVisualStyle = null,
        };
        SpellCheck.SetIsEnabled(box, Editor.Rechtschreibung);

        var host = new Grid { Width = breite * dip, Height = hoehe * dip };
        host.Children.Add(box);
        var zoom = Editor.Zoom;
        var skala = Skala;
        host.RenderTransform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(zoom, zoom),
                new RotateTransform(traeger.Drehung, (traeger.MitteX - ix) * skala, (traeger.MitteY - iy) * skala),
            },
        };
        Canvas.SetLeft(host, ix * skala);
        Canvas.SetTop(host, iy * skala);
        _textEbene.Children.Add(host);

        _textBearbeitung = new RichTextBearbeitung(traeger, box, TextBeendet);
        _textBearbeitung.AuswahlGeaendert += Editor.MeldeTextAuswahl;
        Editor.Textbearbeitung = _textBearbeitung;

        _zeichnung.InvalidateVisual();
        _ueberlagerung.InvalidateVisual();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            box.Focus();
            Keyboard.Focus(box);
            if (traeger.HatText)
                box.CaretPosition = box.Document.ContentEnd;
            Editor.MeldeTextAuswahl();
        });
    }

    private void TextBeendet(RichTextBearbeitung bearbeitung, bool uebernehmen)
    {
        var traeger = bearbeitung.Objekt;
        if (uebernehmen)
        {
            var vorlage = traeger.Absaetze.FirstOrDefault()?.Laeufe.FirstOrDefault()?.Kopie() ?? new Textlauf();
            vorlage.Text = "";
            traeger.Absaetze = bearbeitung.LeseAbsaetze(vorlage);
        }

        _textEbene.Children.Clear();
        _textBearbeitung = null;
        if (ReferenceEquals(Editor.Textbearbeitung, bearbeitung))
            Editor.Textbearbeitung = null;

        _zeichnung.InvalidateVisual();
        _ueberlagerung.InvalidateVisual();
        if (uebernehmen)
            Editor.Abschliessen();
        Editor.MeldeTextAuswahl();

        // Nach Esc zurück auf die Seite, damit Tastenkürzel wieder funktionieren (nicht innerhalb des Fokusereignisses).
        if (bearbeitung.FokusZurueck)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Focus());
    }
}
