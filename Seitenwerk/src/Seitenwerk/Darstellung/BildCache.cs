using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Seitenwerk.Core.Modell;

namespace Seitenwerk.Darstellung;

/// <summary>Dekodiert eingebettete Bilder einmal und hält sie bereit.</summary>
public static class BildCache
{
    private static readonly Dictionary<string, (byte[] Daten, ImageSource? Bild)> Cache = new();

    public static ImageSource? Hole(Medium? medium)
    {
        if (medium == null || medium.Daten.Length == 0)
            return null;
        if (Cache.TryGetValue(medium.Id, out var eintrag) && ReferenceEquals(eintrag.Daten, medium.Daten))
            return eintrag.Bild;

        var bild = Dekodieren(medium);
        Cache[medium.Id] = (medium.Daten, bild);
        return bild;
    }

    public static void Leeren() => Cache.Clear();

    private static ImageSource? Dekodieren(Medium medium)
    {
        try
        {
            var format = BildFormat.Erkennen(medium.Daten) ?? medium.Endung;
            if (format is "emf" or "wmf")
                return Metadatei(medium.Daten);

            using var ms = new MemoryStream(medium.Daten);
            var bild = new BitmapImage();
            bild.BeginInit();
            bild.CacheOption = BitmapCacheOption.OnLoad;
            bild.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bild.StreamSource = ms;
            bild.EndInit();
            bild.Freeze();
            return bild;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException or IOException)
        {
            return null;
        }
    }

    /// <summary>Windows-Metadateien (EMF/WMF) kann WPF nicht direkt laden; sie werden über GDI+ in ein Bitmap gezeichnet.</summary>
    private static ImageSource? Metadatei(byte[] daten)
    {
        try
        {
            using var ms = new MemoryStream(daten);
            using var meta = new System.Drawing.Imaging.Metafile(ms);
            var seitenverhaeltnis = meta.Width > 0 && meta.Height > 0 ? (double)meta.Width / meta.Height : 1;
            var breite = 1600;
            var hoehe = Math.Max(1, (int)Math.Round(breite / seitenverhaeltnis));
            if (hoehe > 3200)
            {
                hoehe = 3200;
                breite = Math.Max(1, (int)Math.Round(hoehe * seitenverhaeltnis));
            }
            using var bitmap = new System.Drawing.Bitmap(breite, hoehe, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(meta, 0, 0, breite, hoehe);
            }
            using var png = new MemoryStream();
            bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            png.Position = 0;
            var bild = new BitmapImage();
            bild.BeginInit();
            bild.CacheOption = BitmapCacheOption.OnLoad;
            bild.StreamSource = png;
            bild.EndInit();
            bild.Freeze();
            return bild;
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.ExternalException or InvalidOperationException or NotSupportedException or IOException)
        {
            return null;
        }
    }
}
