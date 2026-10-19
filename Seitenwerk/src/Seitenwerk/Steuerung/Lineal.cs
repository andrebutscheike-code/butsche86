using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Seitenwerk.Core.Modell;

namespace Seitenwerk.Steuerung;

/// <summary>Waagerechtes oder senkrechtes Lineal in Zentimetern, bezogen auf den Anfang der aktuellen Seite.</summary>
public sealed class Lineal : FrameworkElement
{
    public const double Dicke = 22;

    private static readonly Brush Hintergrund = Frozen(new SolidColorBrush(Color.FromRgb(240, 240, 240)));
    private static readonly Brush Seitenband = Frozen(Brushes.White);
    private static readonly Brush Strich = Frozen(new SolidColorBrush(Color.FromRgb(90, 90, 90)));
    private static readonly Brush Auswahlband = Frozen(new SolidColorBrush(Color.FromArgb(70, 43, 87, 154)));
    private static readonly double[] Schritte = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000 };

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

    public bool Senkrecht { get; init; }
    public EditorSitzung? Editor { get; set; }

    /// <summary>Liefert den Punkt, an dem die aktuelle Seite beginnt, in Koordinaten dieses Lineals.</summary>
    public Func<Point?>? UrsprungQuelle { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        var breite = ActualWidth;
        var hoehe = ActualHeight;
        dc.DrawRectangle(Hintergrund, null, new Rect(0, 0, breite, hoehe));
        var editor = Editor;
        var ursprungPunkt = UrsprungQuelle?.Invoke();
        if (editor == null || ursprungPunkt == null)
            return;

        var skala = editor.Zoom * 96.0 / 25.4;
        var laenge = Senkrecht ? hoehe : breite;
        var ursprung = Senkrecht ? ursprungPunkt.Value.Y : ursprungPunkt.Value.X;
        var seitenLaenge = (Senkrecht ? editor.Dokument.SeitenHoehe : editor.Dokument.SeitenBreite) * skala;

        // Fläche der Seite hell hinterlegen
        var von = Math.Max(0, ursprung);
        var bis = Math.Min(laenge, ursprung + seitenLaenge);
        if (bis > von)
            dc.DrawRectangle(Seitenband, null, Senkrecht ? new Rect(0, von, breite, bis - von) : new Rect(von, 0, bis - von, hoehe));

        // Auswahl hervorheben
        if (editor.Auswahl.Count > 0 && editor.AuswahlSeite is { } seite && editor.Dokument.Seiten.IndexOf(seite) == editor.AktuelleSeite)
        {
            var (x0, y0, x1, y1) = Geometrie.UmgebendesRechteck(editor.Auswahl);
            var a = ursprung + (Senkrecht ? y0 : x0) * skala;
            var b = ursprung + (Senkrecht ? y1 : x1) * skala;
            dc.DrawRectangle(Auswahlband, null, Senkrecht ? new Rect(0, a, breite, Math.Max(1, b - a)) : new Rect(a, 0, Math.Max(1, b - a), hoehe));
        }

        var klein = Schritte.First(s => s * skala >= 6);
        var beschriftung = Schritte.First(s => s * skala >= 50 && s >= 10);
        var halb = beschriftung / 2;

        var startMm = Math.Floor((0 - ursprung) / skala / klein) * klein;
        var endeMm = (laenge - ursprung) / skala;
        var stift = new Pen(Strich, 1);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var schrift = new Typeface("Segoe UI");

        for (var mm = startMm; mm <= endeMm; mm += klein)
        {
            var pos = Math.Round(ursprung + mm * skala) + 0.5;
            if (pos < 0 || pos > laenge)
                continue;

            double tick;
            var istBeschriftung = Math.Abs(mm % beschriftung) < 1e-6;
            if (istBeschriftung)
                tick = Dicke * 0.7;
            else if (Math.Abs(mm % halb) < 1e-6)
                tick = Dicke * 0.45;
            else
                tick = Dicke * 0.25;

            if (Senkrecht)
                dc.DrawLine(stift, new Point(Dicke - tick, pos), new Point(Dicke, pos));
            else
                dc.DrawLine(stift, new Point(pos, Dicke - tick), new Point(pos, Dicke));

            if (istBeschriftung)
            {
                var text = new FormattedText((mm / 10).ToString("0.##", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, schrift, 9, Strich, pixelsPerDip);
                if (Senkrecht)
                {
                    dc.PushTransform(new RotateTransform(-90, 2, pos + 2));
                    dc.DrawText(text, new Point(2, pos + 2 - text.Height / 2 + 4));
                    dc.Pop();
                }
                else
                {
                    dc.DrawText(text, new Point(pos + 3, 1));
                }
            }
        }

        // Untere bzw. rechte Kante
        dc.DrawLine(new Pen(Brushes.Gray, 1), Senkrecht ? new Point(Dicke - 0.5, 0) : new Point(0, Dicke - 0.5),
            Senkrecht ? new Point(Dicke - 0.5, hoehe) : new Point(breite, Dicke - 0.5));
    }
}
