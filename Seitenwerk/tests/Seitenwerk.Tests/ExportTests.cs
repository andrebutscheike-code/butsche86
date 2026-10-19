using System.Text;
using Seitenwerk.Core.Export;

namespace Seitenwerk.Tests;

public class ExportTests
{
    private static string Testdatei(string name) => Path.Combine(AppContext.BaseDirectory, "Testdaten", name);

    [Fact]
    public void Pdf_HatSeitenInDerRichtigenGroesse()
    {
        var jpeg = File.ReadAllBytes(Testdatei("foto.jpg"));
        using var ms = new MemoryStream();
        PdfSchreiber.Schreiben(ms, "Testtitel", new[]
        {
            new SeitenBild(jpeg, 210, 297),
            new SeitenBild(jpeg, 297, 210),
        });

        var bytes = ms.ToArray();
        // Zum Prüfen mit einem fremden PDF-Programm: SEITENWERK_PDF_AUSGABE=/pfad/test.pdf
        var ausgabe = Environment.GetEnvironmentVariable("SEITENWERK_PDF_AUSGABE");
        if (!string.IsNullOrEmpty(ausgabe))
            File.WriteAllBytes(ausgabe, bytes);
        Assert.True(bytes.Length > 500);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));

        var text = Encoding.Latin1.GetString(bytes);
        // 210 mm = 595,28 pt, 297 mm = 841,89 pt
        Assert.Contains("/MediaBox [0 0 595.28 841.89]", text);
        Assert.Contains("/MediaBox [0 0 841.89 595.28]", text);
        Assert.Contains("/Count 2", text);
        Assert.Contains("/Filter /DCTDecode", text);
        Assert.Contains("%%EOF", text);

        // Jeder Eintrag der Verweistabelle muss auf den Anfang seines Objekts zeigen.
        var xref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        var tabellenStart = int.Parse(text[(xref + "startxref".Length)..].Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]);
        var zeilen = text[tabellenStart..].Split('\n');
        var anzahl = int.Parse(zeilen[1].Split(' ')[1]);
        for (var i = 1; i < anzahl; i++)
        {
            var offset = int.Parse(zeilen[2 + i].Split(' ')[0]);
            Assert.StartsWith($"{i} 0 obj", text[offset..]);
        }
    }

    [Fact]
    public void Pdf_OhneSeiten_WirftFehler()
    {
        using var ms = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() => PdfSchreiber.Schreiben(ms, null, Array.Empty<SeitenBild>()));
    }
}
