using System.Windows;
using System.Windows.Media;
using Seitenwerk.Core.Modell;

namespace Seitenwerk.Darstellung;

public sealed class RenderOptionen
{
    /// <summary>Bearbeitungshilfen zeigen (Rahmen leerer Textfelder, Markierung bei zu langem Text). Nie beim Export.</summary>
    public bool Hilfen { get; init; }

    public double PixelsPerDip { get; init; } = 1;

    /// <summary>Id eines Objekts, dessen Text gerade im Editor bearbeitet wird und deshalb nicht gezeichnet werden soll.</summary>
    public string? TextVerbergenFuerId { get; set; }
}

/// <summary>Zeichnet Seiten und Objekte. Das Koordinatensystem sind Millimeter; der Aufrufer legt den Maßstab fest.</summary>
public static class SeitenRenderer
{
    private static readonly Dictionary<string, Geometry?> PfadCache = new();

    public static void ZeichneSeite(DrawingContext dc, Dokument dokument, Seite seite, RenderOptionen optionen)
    {
        var hintergrund = Farben.Pinsel(seite.Hintergrund) ?? Brushes.White;
        dc.DrawRectangle(hintergrund, null, new Rect(0, 0, dokument.SeitenBreite, dokument.SeitenHoehe));
        foreach (var objekt in seite.Objekte)
            ZeichneObjekt(dc, dokument, objekt, optionen);
    }

    public static void ZeichneObjekt(DrawingContext dc, Dokument dokument, Objekt objekt, RenderOptionen optionen)
    {
        var gedreht = objekt.Drehung != 0 && objekt is not GruppeObjekt;
        if (gedreht)
            dc.PushTransform(new RotateTransform(objekt.Drehung, objekt.MitteX, objekt.MitteY));

        switch (objekt)
        {
            case TextfeldObjekt textfeld:
                ZeichneTextfeld(dc, textfeld, optionen);
                break;
            case FormObjekt form:
                ZeichneForm(dc, form, optionen);
                break;
            case LinieObjekt linie:
                ZeichneLinie(dc, linie);
                break;
            case BildObjekt bild:
                ZeichneBild(dc, dokument, bild);
                break;
            case PfadObjekt pfad:
                ZeichnePfad(dc, pfad);
                break;
            case GruppeObjekt gruppe:
                foreach (var kind in gruppe.Kinder)
                    ZeichneObjekt(dc, dokument, kind, optionen);
                break;
        }

        if (gedreht)
            dc.Pop();
    }

    public static Rect RechteckVon(Objekt o) => new(o.X, o.Y, Math.Max(o.Breite, 0.01), Math.Max(o.Hoehe, 0.01));

    // ---------------------------------------------------------------- Stifte

    public static Pen? Stift(string? farbe, double staerkePt, Strichart art)
    {
        if (farbe == null || staerkePt <= 0)
            return null;
        var pinsel = Farben.Pinsel(farbe);
        if (pinsel == null)
            return null;
        var dicke = Math.Max(0.05, staerkePt * TextLayout.MmProPunkt);
        var stift = new Pen(pinsel, dicke);
        switch (art)
        {
            case Strichart.Gestrichelt:
                stift.DashStyle = new DashStyle(new[] { 4.0, 2.0 }, 0);
                break;
            case Strichart.Gepunktet:
                stift.DashStyle = new DashStyle(new[] { 0.1, 2.0 }, 0);
                stift.DashCap = PenLineCap.Round;
                break;
        }
        stift.LineJoin = PenLineJoin.Miter;
        stift.Freeze();
        return stift;
    }

    // ---------------------------------------------------------------- Textfelder und Formen

    private static void ZeichneTextfeld(DrawingContext dc, TextfeldObjekt feld, RenderOptionen optionen)
    {
        var rechteck = RechteckVon(feld);
        var fuellung = Farben.Pinsel(feld.Fuellung, feld.Deckkraft);
        var stift = Stift(feld.Rahmenfarbe, feld.Rahmenbreite, feld.Rahmenart);

        if (fuellung != null || stift != null)
            dc.DrawRectangle(fuellung, stift, rechteck);
        else if (optionen.Hilfen)
            dc.DrawRectangle(null, HilfsStift, rechteck);

        ZeichneText(dc, feld, rechteck, optionen);
    }

    private static readonly Pen HilfsStift = ErzeugeHilfsStift();

    private static Pen ErzeugeHilfsStift()
    {
        var stift = new Pen(new SolidColorBrush(Color.FromRgb(150, 150, 150)), 0.2) { DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0) };
        stift.Freeze();
        return stift;
    }

    private static void ZeichneForm(DrawingContext dc, FormObjekt form, RenderOptionen optionen)
    {
        var r = RechteckVon(form);
        var fuellung = Farben.Pinsel(form.Fuellung, form.Deckkraft);
        var stift = Stift(form.Rahmenfarbe, form.Rahmenbreite, form.Rahmenart);

        switch (form.Form)
        {
            case FormArt.Rechteck:
                dc.DrawRectangle(fuellung, stift, r);
                break;
            case FormArt.AbgerundetesRechteck:
                var radius = Math.Min(form.Eckenradius, Math.Min(r.Width, r.Height) / 2);
                dc.DrawRoundedRectangle(fuellung, stift, r, radius, radius);
                break;
            case FormArt.Ellipse:
                dc.DrawEllipse(fuellung, stift, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
                break;
            default:
                var punkte = Geometrie.Polygon(form.Form);
                if (punkte == null)
                {
                    dc.DrawRectangle(fuellung, stift, r);
                    break;
                }
                dc.DrawGeometry(fuellung, stift, PolygonGeometrie(punkte, r));
                break;
        }

        ZeichneText(dc, form, r, optionen);
    }

    public static Geometry PolygonGeometrie((double X, double Y)[] punkte, Rect r)
    {
        var geometrie = new StreamGeometry();
        using (var ctx = geometrie.Open())
        {
            ctx.BeginFigure(new Point(r.X + punkte[0].X * r.Width, r.Y + punkte[0].Y * r.Height), true, true);
            ctx.PolyLineTo(punkte.Skip(1).Select(p => new Point(r.X + p.X * r.Width, r.Y + p.Y * r.Height)).ToList(), true, false);
        }
        geometrie.Freeze();
        return geometrie;
    }

    private static void ZeichneText(DrawingContext dc, TextTraeger traeger, Rect rechteck, RenderOptionen optionen)
    {
        if (!traeger.HatText || traeger.Id == optionen.TextVerbergenFuerId)
            return;

        var rand = Math.Min(traeger.Innenabstand, Math.Min(rechteck.Width, rechteck.Height) / 2.5);
        var innen = new Rect(rechteck.X + rand, rechteck.Y + rand, Math.Max(1, rechteck.Width - 2 * rand), Math.Max(1, rechteck.Height - 2 * rand));
        var zeilen = TextLayout.Erstellen(traeger.Absaetze, innen.Width, optionen.PixelsPerDip);
        var hoehe = TextLayout.GesamtHoehe(zeilen);

        var y = traeger.VertikaleAusrichtung switch
        {
            VertikaleAusrichtung.Mitte => innen.Y + (innen.Height - hoehe) / 2,
            VertikaleAusrichtung.Unten => innen.Bottom - hoehe,
            _ => innen.Y,
        };

        foreach (var zeile in zeilen)
        {
            dc.DrawText(zeile.Text, new Point(innen.X, y));
            y += zeile.Hoehe + zeile.AbstandNach;
        }

        if (optionen.Hilfen && hoehe > innen.Height + 0.5)
            ZeichneUeberlaufMarke(dc, rechteck);
    }

    private static void ZeichneUeberlaufMarke(DrawingContext dc, Rect rechteck)
    {
        var marke = new Rect(rechteck.Right - 4.5, rechteck.Bottom - 4.5, 4.5, 4.5);
        dc.DrawRectangle(Brushes.White, new Pen(Brushes.Red, 0.25), marke);
        var text = new FormattedText("…", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 3.2, Brushes.Red, 1);
        dc.DrawText(text, new Point(marke.X + (marke.Width - text.Width) / 2, marke.Y + (marke.Height - text.Height) / 2 - 0.4));
    }

    // ---------------------------------------------------------------- Linien

    private static void ZeichneLinie(DrawingContext dc, LinieObjekt linie)
    {
        var stift = Stift(linie.Farbe, linie.Staerke, linie.Art);
        if (stift == null)
            return;
        var (x1, y1, x2, y2) = linie.Punkte;
        var p1 = new Point(x1, y1);
        var p2 = new Point(x2, y2);

        var kappe = linie.Anfang != Pfeilspitze.Keine || linie.Ende != Pfeilspitze.Keine ? PenLineCap.Flat : PenLineCap.Round;
        var angepasst = stift.Clone();
        angepasst.StartLineCap = kappe;
        angepasst.EndLineCap = kappe;
        if (linie.Art == Strichart.Gepunktet)
            angepasst.DashCap = PenLineCap.Round;
        angepasst.Freeze();

        var richtung = p2 - p1;
        var laenge = richtung.Length;
        if (laenge < 0.001)
            return;
        richtung.Normalize();

        var dicke = angepasst.Thickness;
        var spitze = Math.Max(3, dicke * 5);
        var start = p1;
        var ende = p2;
        // Die Linie endet an der Pfeilbasis, damit sie nicht über die Spitze hinausragt.
        if (linie.Anfang == Pfeilspitze.Pfeil && laenge > spitze)
            start = p1 + richtung * spitze * 0.8;
        if (linie.Ende == Pfeilspitze.Pfeil && laenge > spitze)
            ende = p2 - richtung * spitze * 0.8;

        dc.DrawLine(angepasst, start, ende);
        var pinsel = angepasst.Brush;
        ZeichneSpitze(dc, linie.Anfang, p1, -richtung, pinsel, spitze);
        ZeichneSpitze(dc, linie.Ende, p2, richtung, pinsel, spitze);
    }

    private static void ZeichneSpitze(DrawingContext dc, Pfeilspitze art, Point spitze, Vector richtung, Brush pinsel, double groesse)
    {
        switch (art)
        {
            case Pfeilspitze.Pfeil:
            {
                var senkrecht = new Vector(-richtung.Y, richtung.X);
                var basis = spitze - richtung * groesse;
                var geometrie = new StreamGeometry();
                using (var ctx = geometrie.Open())
                {
                    ctx.BeginFigure(spitze, true, true);
                    ctx.LineTo(basis + senkrecht * groesse * 0.4, false, false);
                    ctx.LineTo(basis - senkrecht * groesse * 0.4, false, false);
                }
                geometrie.Freeze();
                dc.DrawGeometry(pinsel, null, geometrie);
                break;
            }
            case Pfeilspitze.Kreis:
                dc.DrawEllipse(pinsel, null, spitze, groesse * 0.35, groesse * 0.35);
                break;
        }
    }

    // ---------------------------------------------------------------- Bilder

    private static void ZeichneBild(DrawingContext dc, Dokument dokument, BildObjekt bild)
    {
        var ziel = RechteckVon(bild);
        var quelle = BildCache.Hole(dokument.FindeMedium(bild.MedienId));

        if (quelle == null)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(238, 238, 238)), new Pen(Brushes.Gray, 0.3), ziel);
            dc.DrawLine(new Pen(Brushes.LightGray, 0.3), ziel.TopLeft, ziel.BottomRight);
            dc.DrawLine(new Pen(Brushes.LightGray, 0.3), ziel.TopRight, ziel.BottomLeft);
            return;
        }

        var sichtbarBreite = Math.Max(0.05, 1 - bild.BeschnittLinks - bild.BeschnittRechts);
        var sichtbarHoehe = Math.Max(0.05, 1 - bild.BeschnittOben - bild.BeschnittUnten);
        var gesamt = new Rect(
            ziel.X - bild.BeschnittLinks / sichtbarBreite * ziel.Width,
            ziel.Y - bild.BeschnittOben / sichtbarHoehe * ziel.Height,
            ziel.Width / sichtbarBreite,
            ziel.Height / sichtbarHoehe);

        var beschnitten = bild.BeschnittLinks > 0 || bild.BeschnittRechts > 0 || bild.BeschnittOben > 0 || bild.BeschnittUnten > 0;
        if (beschnitten)
            dc.PushClip(new RectangleGeometry(ziel));
        if (bild.Deckkraft < 1)
            dc.PushOpacity(bild.Deckkraft);
        dc.DrawImage(quelle, gesamt);
        if (bild.Deckkraft < 1)
            dc.Pop();
        if (beschnitten)
            dc.Pop();

        var stift = Stift(bild.Rahmenfarbe, bild.Rahmenbreite, bild.Rahmenart);
        if (stift != null)
            dc.DrawRectangle(null, stift, ziel);
    }

    // ---------------------------------------------------------------- Pfade

    private static Geometry? PfadGeometrie(string daten)
    {
        if (PfadCache.TryGetValue(daten, out var vorhanden))
            return vorhanden;
        Geometry? geometrie = null;
        try
        {
            geometrie = Geometry.Parse(daten);
            if (geometrie.CanFreeze)
                geometrie.Freeze();
        }
        catch (FormatException)
        {
        }
        PfadCache[daten] = geometrie;
        return geometrie;
    }

    private static void ZeichnePfad(DrawingContext dc, PfadObjekt pfad)
    {
        var geometrie = PfadGeometrie(pfad.Daten);
        if (geometrie == null || pfad.ViewBreite <= 0 || pfad.ViewHoehe <= 0)
            return;

        var sx = pfad.Breite / pfad.ViewBreite;
        var sy = pfad.Hoehe / pfad.ViewHoehe;
        var matrix = new Matrix(sx, 0, 0, sy, pfad.X - pfad.ViewX * sx, pfad.Y - pfad.ViewY * sy);

        var fuellung = Farben.Pinsel(pfad.Fuellung, pfad.Deckkraft);
        var stift = Stift(pfad.Rahmenfarbe, pfad.Rahmenbreite, pfad.Rahmenart);
        if (stift != null)
        {
            // Die Transformation skaliert auch die Strichstärke, deshalb wird sie hier ausgeglichen.
            var mittel = Math.Sqrt(Math.Abs(sx * sy));
            if (mittel > 0)
            {
                var angepasst = stift.Clone();
                angepasst.Thickness = stift.Thickness / mittel;
                angepasst.Freeze();
                stift = angepasst;
            }
        }

        dc.PushTransform(new MatrixTransform(matrix));
        dc.DrawGeometry(fuellung, stift, geometrie);
        dc.Pop();
    }
}
