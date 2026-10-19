namespace Seitenwerk.Core.Modell;

public static class Geometrie
{
    /// <summary>Dreht einen Punkt um (cx, cy); positive Winkel drehen im Uhrzeigersinn (y-Achse zeigt nach unten).</summary>
    public static (double X, double Y) Drehen(double px, double py, double cx, double cy, double grad)
    {
        if (grad == 0)
            return (px, py);
        var rad = grad * Math.PI / 180;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var dx = px - cx;
        var dy = py - cy;
        return (cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
    }

    /// <summary>Die vier Ecken des gedrehten Objekts: links oben, rechts oben, rechts unten, links unten.</summary>
    public static (double X, double Y)[] Ecken(Objekt o)
    {
        var cx = o.MitteX;
        var cy = o.MitteY;
        return new[]
        {
            Drehen(o.X, o.Y, cx, cy, o.Drehung),
            Drehen(o.X + o.Breite, o.Y, cx, cy, o.Drehung),
            Drehen(o.X + o.Breite, o.Y + o.Hoehe, cx, cy, o.Drehung),
            Drehen(o.X, o.Y + o.Hoehe, cx, cy, o.Drehung),
        };
    }

    /// <summary>Kleinstes achsenparalleles Rechteck um das (gedrehte) Objekt: (x0, y0, x1, y1).</summary>
    public static (double X0, double Y0, double X1, double Y1) UmgebendesRechteck(Objekt o)
    {
        if (o.Drehung == 0)
            return (o.X, o.Y, o.X + o.Breite, o.Y + o.Hoehe);
        var e = Ecken(o);
        return (e.Min(p => p.X), e.Min(p => p.Y), e.Max(p => p.X), e.Max(p => p.Y));
    }

    public static (double X0, double Y0, double X1, double Y1) UmgebendesRechteck(IEnumerable<Objekt> objekte)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var o in objekte)
        {
            var r = UmgebendesRechteck(o);
            x0 = Math.Min(x0, r.X0);
            y0 = Math.Min(y0, r.Y0);
            x1 = Math.Max(x1, r.X1);
            y1 = Math.Max(y1, r.Y1);
        }
        return (x0, y0, x1, y1);
    }

    /// <summary>Trifft der Punkt (in Seitenkoordinaten) das Objekt? Toleranz in Millimetern.</summary>
    public static bool Trifft(Objekt o, double px, double py, double toleranz)
    {
        if (o is GruppeObjekt g)
            return g.Kinder.Any(k => Trifft(k, px, py, toleranz));

        // Punkt ins ungedrehte Koordinatensystem des Objekts zurückdrehen.
        var (lx, ly) = Drehen(px, py, o.MitteX, o.MitteY, -o.Drehung);

        if (o is LinieObjekt linie)
        {
            var (x1, y1, x2, y2) = linie.Punkte;
            var breite = Math.Max(toleranz, linie.Staerke * 25.4 / 72 / 2);
            return AbstandZurStrecke(lx, ly, x1, y1, x2, y2) <= breite;
        }

        if (o is FormObjekt { Form: FormArt.Ellipse } && o.Breite > 0 && o.Hoehe > 0)
        {
            var rx = o.Breite / 2 + toleranz;
            var ry = o.Hoehe / 2 + toleranz;
            var dx = (lx - o.MitteX) / rx;
            var dy = (ly - o.MitteY) / ry;
            return dx * dx + dy * dy <= 1;
        }

        return lx >= o.X - toleranz && lx <= o.X + o.Breite + toleranz
            && ly >= o.Y - toleranz && ly <= o.Y + o.Hoehe + toleranz;
    }

    public static double AbstandZurStrecke(double px, double py, double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var laenge2 = dx * dx + dy * dy;
        if (laenge2 < 1e-12)
            return Math.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));
        var t = Math.Clamp(((px - x1) * dx + (py - y1) * dy) / laenge2, 0, 1);
        var qx = x1 + t * dx;
        var qy = y1 + t * dy;
        return Math.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
    }

    /// <summary>Umrisspunkte (0..1) für Polygonformen; null bei Rechteck, abgerundetem Rechteck und Ellipse.</summary>
    public static (double X, double Y)[]? Polygon(FormArt form)
    {
        switch (form)
        {
            case FormArt.Dreieck:
                return new[] { (0.5, 0.0), (1.0, 1.0), (0.0, 1.0) };
            case FormArt.RechtwinkligesDreieck:
                return new[] { (0.0, 0.0), (1.0, 1.0), (0.0, 1.0) };
            case FormArt.Raute:
                return new[] { (0.5, 0.0), (1.0, 0.5), (0.5, 1.0), (0.0, 0.5) };
            case FormArt.Fuenfeck:
                return Regelmaessig(5);
            case FormArt.Sechseck:
                return Regelmaessig(6);
            case FormArt.Pfeil:
                return new[]
                {
                    (0.0, 0.3), (0.6, 0.3), (0.6, 0.0), (1.0, 0.5), (0.6, 1.0), (0.6, 0.7), (0.0, 0.7),
                };
            case FormArt.Stern:
                return Stern(5, 0.4);
            default:
                return null;
        }
    }

    private static (double X, double Y)[] Regelmaessig(int ecken)
    {
        var punkte = new (double X, double Y)[ecken];
        for (var i = 0; i < ecken; i++)
        {
            var w = -Math.PI / 2 + 2 * Math.PI * i / ecken;
            punkte[i] = (Math.Cos(w), Math.Sin(w));
        }
        return Normieren(punkte);
    }

    private static (double X, double Y)[] Stern(int spitzen, double innenRadius)
    {
        var punkte = new (double X, double Y)[spitzen * 2];
        for (var i = 0; i < spitzen * 2; i++)
        {
            var w = -Math.PI / 2 + Math.PI * i / spitzen;
            var r = i % 2 == 0 ? 1.0 : innenRadius;
            punkte[i] = (r * Math.Cos(w), r * Math.Sin(w));
        }
        return Normieren(punkte);
    }

    private static (double X, double Y)[] Normieren((double X, double Y)[] punkte)
    {
        var minX = punkte.Min(p => p.X);
        var maxX = punkte.Max(p => p.X);
        var minY = punkte.Min(p => p.Y);
        var maxY = punkte.Max(p => p.Y);
        return punkte.Select(p => ((p.X - minX) / (maxX - minX), (p.Y - minY) / (maxY - minY))).ToArray();
    }
}
