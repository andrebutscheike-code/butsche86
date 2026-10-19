using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Seitenwerk.Core.Export;
using Seitenwerk.Core.Modell;
using Seitenwerk.Darstellung;

namespace Seitenwerk.Werkzeuge;

/// <summary>Rendert Seiten für Vorschau, Bilddateien, PDF und Druck.</summary>
public static class Exporter
{
    private const double MaximalePixel = 60_000_000;

    /// <summary>Rendert eine Seite ohne Bearbeitungshilfen. Die Pixelzahl wird begrenzt, indem die Auflösung bei großen Seiten verringert wird.</summary>
    public static BitmapSource RenderSeite(Dokument dokument, Seite seite, double dpi)
    {
        var zoll = dokument.SeitenBreite / 25.4 * (dokument.SeitenHoehe / 25.4);
        dpi = Math.Min(dpi, Math.Sqrt(MaximalePixel / Math.Max(zoll, 0.01)));
        dpi = Math.Max(dpi, 12);

        var breite = Math.Max(1, (int)Math.Round(dokument.SeitenBreite / 25.4 * dpi));
        var hoehe = Math.Max(1, (int)Math.Round(dokument.SeitenHoehe / 25.4 * dpi));

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Ideal);
        using (var dc = visual.RenderOpen())
        {
            // Der Maßstab bildet Millimeter auf geräteunabhängige Einheiten (1/96 Zoll) ab.
            var s = 96.0 / 25.4;
            dc.PushTransform(new ScaleTransform(s, s));
            SeitenRenderer.ZeichneSeite(dc, dokument, seite, new RenderOptionen { Hilfen = false, PixelsPerDip = dpi / 96.0 });
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(breite, hoehe, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static byte[] AlsJpeg(BitmapSource bild, int qualitaet = 92)
    {
        var grau = new FormatConvertedBitmap(bild, PixelFormats.Bgr24, null, 0);
        var encoder = new JpegBitmapEncoder { QualityLevel = qualitaet };
        encoder.Frames.Add(BitmapFrame.Create(grau));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public static byte[] AlsPng(BitmapSource bild)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bild));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public static void PdfSpeichern(Dokument dokument, string pfad, double dpi = 300, IProgress<int>? fortschritt = null)
    {
        var seiten = new List<SeitenBild>();
        for (var i = 0; i < dokument.Seiten.Count; i++)
        {
            var bild = RenderSeite(dokument, dokument.Seiten[i], dpi);
            seiten.Add(new SeitenBild(AlsJpeg(bild), dokument.SeitenBreite, dokument.SeitenHoehe));
            fortschritt?.Report(i + 1);
        }
        PdfSchreiber.Schreiben(pfad, dokument.Titel, seiten);
    }

    /// <summary>Speichert jede Seite als PNG-Datei "Name_Seite1.png" usw. und gibt die Dateinamen zurück.</summary>
    public static List<string> PngSpeichern(Dokument dokument, string ordner, string basisName, double dpi = 200)
    {
        var dateien = new List<string>();
        for (var i = 0; i < dokument.Seiten.Count; i++)
        {
            var datei = Path.Combine(ordner, dokument.Seiten.Count == 1 ? $"{basisName}.png" : $"{basisName}_Seite{i + 1}.png");
            File.WriteAllBytes(datei, AlsPng(RenderSeite(dokument, dokument.Seiten[i], dpi)));
            dateien.Add(datei);
        }
        return dateien;
    }

    /// <summary>Kleine Vorschau für die Seitenleiste.</summary>
    public static BitmapSource Vorschau(Dokument dokument, Seite seite, double breitePixel)
    {
        var dpi = breitePixel / (dokument.SeitenBreite / 25.4);
        return RenderSeite(dokument, seite, dpi);
    }

    public static void Drucken(Dokument dokument, string titel)
    {
        var dialog = new PrintDialog
        {
            UserPageRangeEnabled = dokument.Seiten.Count > 1,
            MinPage = 1,
            MaxPage = (uint)dokument.Seiten.Count,
            PageRangeSelection = PageRangeSelection.AllPages,
        };
        if (dialog.ShowDialog() != true)
            return;

        var papier = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
        // Hochformat/Querformat des Papiers passend zur Seite wählen.
        var seiteQuer = dokument.SeitenBreite > dokument.SeitenHoehe;
        var papierQuer = papier.Width > papier.Height;
        if (seiteQuer != papierQuer && dialog.PrintQueue != null)
        {
            try
            {
                dialog.PrintTicket.PageOrientation = seiteQuer ? PageOrientation.Landscape : PageOrientation.Portrait;
                papier = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            }
            catch (PrintQueueException)
            {
            }
        }

        var von = dialog.PageRangeSelection == PageRangeSelection.UserPages ? dialog.PageRange.PageFrom : 1;
        var bis = dialog.PageRangeSelection == PageRangeSelection.UserPages ? dialog.PageRange.PageTo : dokument.Seiten.Count;
        dialog.PrintDocument(new DokumentPaginator(dokument, papier, von - 1, bis - 1), titel);
    }
}

/// <summary>Teilt ein Dokument für den Druck in Seiten; jede Seite wird maßstäblich verkleinert, falls sie nicht aufs Papier passt.</summary>
internal sealed class DokumentPaginator : DocumentPaginator
{
    private readonly Dokument _dokument;
    private readonly int _erste;
    private readonly int _anzahl;

    public DokumentPaginator(Dokument dokument, Size papier, int ersteSeite, int letzteSeite)
    {
        _dokument = dokument;
        PageSize = papier;
        _erste = Math.Clamp(ersteSeite, 0, dokument.Seiten.Count - 1);
        _anzahl = Math.Max(1, Math.Min(letzteSeite, dokument.Seiten.Count - 1) - _erste + 1);
    }

    public override bool IsPageCountValid => true;
    public override int PageCount => _anzahl;
    public override Size PageSize { get; set; }
    public override IDocumentPaginatorSource? Source => null;

    public override DocumentPage GetPage(int pageNumber)
    {
        var seite = _dokument.Seiten[_erste + pageNumber];
        const double mmZuDip = 96.0 / 25.4;
        var breite = _dokument.SeitenBreite * mmZuDip;
        var hoehe = _dokument.SeitenHoehe * mmZuDip;
        var faktor = Math.Min(1.0, Math.Min(PageSize.Width / breite, PageSize.Height / hoehe));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform((PageSize.Width - breite * faktor) / 2, (PageSize.Height - hoehe * faktor) / 2));
            dc.PushTransform(new ScaleTransform(mmZuDip * faktor, mmZuDip * faktor));
            SeitenRenderer.ZeichneSeite(dc, _dokument, seite, new RenderOptionen { Hilfen = false, PixelsPerDip = 2 });
            dc.Pop();
            dc.Pop();
        }
        return new DocumentPage(visual, PageSize, new Rect(PageSize), new Rect(PageSize));
    }
}
