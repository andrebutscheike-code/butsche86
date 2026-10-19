namespace Seitenwerk.Core.Modell;

public readonly record struct BildInfo(int BreitePixel, int HoehePixel, double DpiX, double DpiY);

/// <summary>Erkennt Bildformate und liest Größe und Auflösung aus den Dateikopfzeilen, ohne das Bild zu dekodieren.</summary>
public static class BildFormat
{
    /// <summary>Dateiendung ohne Punkt (png, jpg, gif, bmp, tif, emf, wmf, svg) oder null.</summary>
    public static string? Erkennen(byte[] d)
    {
        if (d.Length < 12)
            return null;
        if (d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47)
            return "png";
        if (d[0] == 0xFF && d[1] == 0xD8)
            return "jpg";
        if (d[0] == 'G' && d[1] == 'I' && d[2] == 'F' && d[3] == '8')
            return "gif";
        if (d[0] == 'B' && d[1] == 'M')
            return "bmp";
        if ((d[0] == 'I' && d[1] == 'I' && d[2] == 42 && d[3] == 0) || (d[0] == 'M' && d[1] == 'M' && d[2] == 0 && d[3] == 42))
            return "tif";
        if (d[0] == 0xD7 && d[1] == 0xCD && d[2] == 0xC6 && d[3] == 0x9A)
            return "wmf";
        if (d.Length >= 44 && d[0] == 1 && d[1] == 0 && d[2] == 0 && d[3] == 0 && d[40] == ' ' && d[41] == 'E' && d[42] == 'M' && d[43] == 'F')
            return "emf";
        var kopf = System.Text.Encoding.UTF8.GetString(d, 0, Math.Min(d.Length, 256));
        if (kopf.Contains("<svg", StringComparison.OrdinalIgnoreCase))
            return "svg";
        return null;
    }

    public static BildInfo? Groesse(byte[] d)
    {
        return Erkennen(d) switch
        {
            "png" => Png(d),
            "jpg" => Jpeg(d),
            "gif" => d.Length >= 10 ? new BildInfo(d[6] | d[7] << 8, d[8] | d[9] << 8, 96, 96) : null,
            "bmp" => Bmp(d),
            _ => null,
        };
    }

    private static int Be32(byte[] d, int o) => d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3];

    private static BildInfo? Png(byte[] d)
    {
        if (d.Length < 24)
            return null;
        var breite = Be32(d, 16);
        var hoehe = Be32(d, 20);
        double dpiX = 96, dpiY = 96;
        var o = 8;
        while (o + 12 <= d.Length)
        {
            var laenge = Be32(d, o);
            if (laenge < 0 || o + 12 + laenge > d.Length)
                break;
            var typ = System.Text.Encoding.ASCII.GetString(d, o + 4, 4);
            if (typ == "pHYs" && laenge >= 9 && d[o + 16] == 1)
            {
                dpiX = Be32(d, o + 8) * 0.0254;
                dpiY = Be32(d, o + 12) * 0.0254;
                break;
            }
            if (typ is "IDAT" or "IEND")
                break;
            o += 12 + laenge;
        }
        return new BildInfo(breite, hoehe, dpiX > 1 ? dpiX : 96, dpiY > 1 ? dpiY : 96);
    }

    private static BildInfo? Jpeg(byte[] d)
    {
        double dpiX = 96, dpiY = 96;
        var o = 2;
        while (o + 4 < d.Length)
        {
            if (d[o] != 0xFF)
            {
                o++;
                continue;
            }
            var marker = d[o + 1];
            if (marker == 0xFF)
            {
                o++;
                continue;
            }
            if (marker is 0xD8 or 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                o += 2;
                continue;
            }
            var laenge = d[o + 2] << 8 | d[o + 3];
            if (marker == 0xE0 && laenge >= 16 && d[o + 4] == 'J' && d[o + 5] == 'F' && d[o + 6] == 'I' && d[o + 7] == 'F')
            {
                var einheit = d[o + 11];
                var x = d[o + 12] << 8 | d[o + 13];
                var y = d[o + 14] << 8 | d[o + 15];
                if (einheit == 1 && x > 1 && y > 1)
                {
                    dpiX = x;
                    dpiY = y;
                }
                else if (einheit == 2 && x > 1 && y > 1)
                {
                    dpiX = x * 2.54;
                    dpiY = y * 2.54;
                }
            }
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC && o + 9 < d.Length)
            {
                var hoehe = d[o + 5] << 8 | d[o + 6];
                var breite = d[o + 7] << 8 | d[o + 8];
                return new BildInfo(breite, hoehe, dpiX, dpiY);
            }
            o += 2 + laenge;
        }
        return null;
    }

    private static BildInfo? Bmp(byte[] d)
    {
        if (d.Length < 46)
            return null;
        int Le32(int o) => d[o] | d[o + 1] << 8 | d[o + 2] << 16 | d[o + 3] << 24;
        var breite = Le32(18);
        var hoehe = Math.Abs(Le32(22));
        var ppmX = Le32(38);
        var ppmY = Le32(42);
        return new BildInfo(breite, hoehe, ppmX > 0 ? ppmX * 0.0254 : 96, ppmY > 0 ? ppmY * 0.0254 : 96);
    }
}
