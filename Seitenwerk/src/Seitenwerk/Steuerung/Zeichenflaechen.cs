using System.Windows;
using System.Windows.Media;
using Seitenwerk.Core.Modell;
using Seitenwerk.Darstellung;

namespace Seitenwerk.Steuerung;

/// <summary>Zeichnet den Seiteninhalt samt Rand- und Rasterhilfen.</summary>
internal sealed class SeitenZeichnung : FrameworkElement
{
    private readonly SeitenAnsicht _ansicht;

    public SeitenZeichnung(SeitenAnsicht ansicht)
    {
        _ansicht = ansicht;
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var dokument = _ansicht.Dokument;
        var seite = _ansicht.Seite;
        if (seite == null)
            return;

        var skala = _ansicht.Skala;
        var optionen = new RenderOptionen
        {
            Hilfen = true,
            PixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip,
            TextVerbergenFuerId = _ansicht.BearbeiteteObjektId,
        };

        dc.PushTransform(new ScaleTransform(skala, skala));
        SeitenRenderer.ZeichneSeite(dc, dokument, seite, optionen);

        var editor = _ansicht.Editor;
        var haar = 1.0 / skala; // eine Bildschirmeinheit in Millimetern

        if (editor.ZeigeRaster)
        {
            var abstand = SeitenAnsicht.RasterAbstand;
            var stift = new Pen(new SolidColorBrush(Color.FromArgb(70, 0, 112, 192)), haar);
            stift.Freeze();
            for (var x = abstand; x < dokument.SeitenBreite; x += abstand)
                dc.DrawLine(stift, new Point(x, 0), new Point(x, dokument.SeitenHoehe));
            for (var y = abstand; y < dokument.SeitenHoehe; y += abstand)
                dc.DrawLine(stift, new Point(0, y), new Point(dokument.SeitenBreite, y));
        }

        if (editor.ZeigeRaender)
        {
            var r = dokument.Raender;
            var stift = new Pen(new SolidColorBrush(Color.FromRgb(88, 130, 200)), haar) { DashStyle = new DashStyle(new[] { 3.0, 3.0 }, 0) };
            stift.Freeze();
            dc.DrawRectangle(null, stift, new Rect(r.Links, r.Oben,
                Math.Max(0, dokument.SeitenBreite - r.Links - r.Rechts),
                Math.Max(0, dokument.SeitenHoehe - r.Oben - r.Unten)));
        }

        dc.Pop();
    }
}

/// <summary>Zeichnet Auswahlrahmen, Griffe, Fanglinien und Vorschauen über dem Seiteninhalt (in Bildschirmeinheiten).</summary>
internal sealed class AuswahlZeichnung : FrameworkElement
{
    public const double GriffGroesse = 8;
    public const double DrehAbstand = 24;

    private static readonly Brush Blau = new SolidColorBrush(Color.FromRgb(43, 87, 154));
    private static readonly Brush HellBlau = new SolidColorBrush(Color.FromRgb(120, 170, 235));
    private static readonly Brush Fang = new SolidColorBrush(Color.FromRgb(230, 30, 140));

    private readonly SeitenAnsicht _ansicht;

    static AuswahlZeichnung()
    {
        Blau.Freeze();
        HellBlau.Freeze();
        Fang.Freeze();
    }

    public AuswahlZeichnung(SeitenAnsicht ansicht)
    {
        _ansicht = ansicht;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var skala = _ansicht.Skala;
        Point P(double x, double y) => new(x * skala, y * skala);

        var umriss = new Pen(Blau, 1);
        var duenn = new Pen(HellBlau, 1);

        // Überfahrenes Objekt
        if (_ansicht.Ueberfahren is { } hover && !_ansicht.Auswahl.Contains(hover))
            ZeichneUmriss(dc, hover, duenn, P);

        var ausgewaehlt = _ansicht.Auswahl;
        if (ausgewaehlt.Count == 1)
        {
            var objekt = ausgewaehlt[0];
            ZeichneUmriss(dc, objekt, umriss, P);
            if (!objekt.Gesperrt && !_ansicht.TextWirdBearbeitet)
                foreach (var (_, punkt) in _ansicht.Griffe(objekt))
                    ZeichneGriff(dc, new Point(punkt.X * skala, punkt.Y * skala), Blau);
            if (!objekt.Gesperrt && !_ansicht.TextWirdBearbeitet && SeitenAnsicht.KannDrehen(objekt))
            {
                var (rx, ry, ax, ay) = _ansicht.DrehGriff(objekt);
                dc.DrawLine(new Pen(Blau, 1), new Point(ax * skala, ay * skala), new Point(rx * skala, ry * skala));
                dc.DrawEllipse(Brushes.White, new Pen(Blau, 1.2), new Point(rx * skala, ry * skala), 5, 5);
            }
        }
        else if (ausgewaehlt.Count > 1)
        {
            foreach (var objekt in ausgewaehlt)
                ZeichneUmriss(dc, objekt, duenn, P);
            var (x0, y0, x1, y1) = Geometrie.UmgebendesRechteck(ausgewaehlt);
            var gestrichelt = new Pen(Blau, 1) { DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0) };
            dc.DrawRectangle(null, gestrichelt, new Rect(P(x0, y0), P(x1, y1)));
        }

        // Fanglinien
        foreach (var (senkrecht, position) in _ansicht.Fanglinien)
        {
            var pen = new Pen(Fang, 1);
            if (senkrecht)
                dc.DrawLine(pen, P(position, 0), P(position, _ansicht.Dokument.SeitenHoehe));
            else
                dc.DrawLine(pen, P(0, position), P(_ansicht.Dokument.SeitenBreite, position));
        }

        // Aufziehrahmen und Vorschau neuer Objekte
        if (_ansicht.Gummiband is { } band)
        {
            var fuellung = new SolidColorBrush(Color.FromArgb(40, 43, 87, 154));
            dc.DrawRectangle(fuellung, new Pen(Blau, 1), new Rect(P(band.Left, band.Top), P(band.Right, band.Bottom)));
        }

        if (_ansicht.Vorschau is { } vorschau)
        {
            var gestrichelt = new Pen(Blau, 1) { DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0) };
            if (vorschau.Linie)
                dc.DrawLine(gestrichelt, P(vorschau.X1, vorschau.Y1), P(vorschau.X2, vorschau.Y2));
            else
                dc.DrawRectangle(null, gestrichelt, new Rect(P(vorschau.X1, vorschau.Y1), P(vorschau.X2, vorschau.Y2)));
        }
    }

    private static void ZeichneUmriss(DrawingContext dc, Objekt objekt, Pen stift, Func<double, double, Point> p)
    {
        if (objekt is LinieObjekt linie)
        {
            var (x1, y1, x2, y2) = linie.Punkte;
            dc.DrawLine(stift, p(x1, y1), p(x2, y2));
            return;
        }

        var ecken = Geometrie.Ecken(objekt);
        var geometrie = new StreamGeometry();
        using (var ctx = geometrie.Open())
        {
            ctx.BeginFigure(p(ecken[0].X, ecken[0].Y), false, true);
            ctx.LineTo(p(ecken[1].X, ecken[1].Y), true, false);
            ctx.LineTo(p(ecken[2].X, ecken[2].Y), true, false);
            ctx.LineTo(p(ecken[3].X, ecken[3].Y), true, false);
        }
        geometrie.Freeze();
        dc.DrawGeometry(null, stift, geometrie);
    }

    private static void ZeichneGriff(DrawingContext dc, Point mitte, Brush rand)
    {
        var r = new Rect(mitte.X - GriffGroesse / 2, mitte.Y - GriffGroesse / 2, GriffGroesse, GriffGroesse);
        dc.DrawRectangle(Brushes.White, new Pen(rand, 1.2), r);
    }
}
