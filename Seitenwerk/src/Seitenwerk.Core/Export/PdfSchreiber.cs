using System.Globalization;
using System.Text;
using Seitenwerk.Core.Modell;

namespace Seitenwerk.Core.Export;

/// <summary>Eine fertig gerenderte Seite (JPEG, RGB oder Graustufen) mit ihrer Größe in Millimetern.</summary>
public sealed record SeitenBild(byte[] Jpeg, double BreiteMm, double HoeheMm);

/// <summary>
/// Schreibt ein PDF, dessen Seiten je aus einem JPEG-Bild bestehen. Das PDF wird ohne Fremdbibliothek erzeugt,
/// weil dafür nur wenige Objekte nötig sind.
/// </summary>
public static class PdfSchreiber
{
    private static readonly Encoding Latin1 = Encoding.Latin1;

    public static void Schreiben(string pfad, string? titel, IEnumerable<SeitenBild> seiten)
    {
        using var datei = File.Create(pfad);
        Schreiben(datei, titel, seiten);
    }

    public static void Schreiben(Stream ziel, string? titel, IEnumerable<SeitenBild> seiten)
    {
        var liste = seiten.ToList();
        if (liste.Count == 0)
            throw new InvalidOperationException("Das Dokument enthält keine Seiten.");

        // Objektnummern: 1 Katalog, 2 Seitenbaum, 3 Info, danach je Seite drei Objekte (Seite, Inhalt, Bild).
        var offsets = new List<long>();
        long position = 0;

        void Schreibe(byte[] daten)
        {
            ziel.Write(daten, 0, daten.Length);
            position += daten.Length;
        }

        void Text(string text) => Schreibe(Latin1.GetBytes(text));

        void BeginneObjekt(int nummer)
        {
            while (offsets.Count < nummer)
                offsets.Add(0);
            offsets[nummer - 1] = position;
            Text($"{nummer} 0 obj\n");
        }

        Text("%PDF-1.4\n");
        Schreibe(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });

        BeginneObjekt(1);
        Text("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        BeginneObjekt(2);
        var kinder = string.Join(" ", Enumerable.Range(0, liste.Count).Select(i => $"{4 + i * 3} 0 R"));
        Text($"<< /Type /Pages /Kids [{kinder}] /Count {liste.Count} >>\nendobj\n");

        BeginneObjekt(3);
        Text("<< /Producer (Seitenwerk)");
        if (!string.IsNullOrWhiteSpace(titel))
            Text(" /Title " + Utf16Hex(titel));
        Text(" >>\nendobj\n");

        for (var i = 0; i < liste.Count; i++)
        {
            var seite = liste[i];
            var info = BildFormat.Groesse(seite.Jpeg);
            if (BildFormat.Erkennen(seite.Jpeg) != "jpg" || info == null)
                throw new InvalidOperationException($"Seite {i + 1} ist kein gültiges JPEG-Bild.");

            var breite = Pt(seite.BreiteMm);
            var hoehe = Pt(seite.HoeheMm);
            var seitenNr = 4 + i * 3;
            var inhaltNr = seitenNr + 1;
            var bildNr = seitenNr + 2;

            BeginneObjekt(seitenNr);
            Text($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {breite} {hoehe}] /Resources << /XObject << /Im0 {bildNr} 0 R >> >> /Contents {inhaltNr} 0 R >>\nendobj\n");

            var inhalt = $"q {breite} 0 0 {hoehe} 0 0 cm /Im0 Do Q\n";
            BeginneObjekt(inhaltNr);
            Text($"<< /Length {Latin1.GetByteCount(inhalt)} >>\nstream\n{inhalt}endstream\nendobj\n");

            BeginneObjekt(bildNr);
            var farbraum = Komponenten(seite.Jpeg) == 1 ? "/DeviceGray" : "/DeviceRGB";
            Text($"<< /Type /XObject /Subtype /Image /Width {info.Value.BreitePixel} /Height {info.Value.HoehePixel} /ColorSpace {farbraum} /BitsPerComponent 8 /Filter /DCTDecode /Length {seite.Jpeg.Length} >>\nstream\n");
            Schreibe(seite.Jpeg);
            Text("\nendstream\nendobj\n");
        }

        var xrefPosition = position;
        Text($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets)
            Text($"{o:D10} 00000 n \n");
        Text($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info 3 0 R >>\nstartxref\n{xrefPosition}\n%%EOF\n");
        ziel.Flush();
    }

    private static string Pt(double mm) => (mm * 72 / 25.4).ToString("0.##", CultureInfo.InvariantCulture);

    private static string Utf16Hex(string text)
    {
        var sb = new StringBuilder("<FEFF");
        foreach (var b in Encoding.BigEndianUnicode.GetBytes(text))
            sb.Append(b.ToString("X2"));
        return sb.Append('>').ToString();
    }

    /// <summary>Anzahl der Farbkomponenten aus dem SOF-Segment (1 = Graustufen, 3 = RGB).</summary>
    private static int Komponenten(byte[] jpeg)
    {
        var o = 2;
        while (o + 9 < jpeg.Length)
        {
            if (jpeg[o] != 0xFF)
            {
                o++;
                continue;
            }
            var marker = jpeg[o + 1];
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                return jpeg[o + 9];
            if (marker is 0xD8 or 0x01 or 0xFF || (marker >= 0xD0 && marker <= 0xD7))
            {
                o += marker == 0xFF ? 1 : 2;
                continue;
            }
            o += 2 + (jpeg[o + 2] << 8 | jpeg[o + 3]);
        }
        return 3;
    }
}
