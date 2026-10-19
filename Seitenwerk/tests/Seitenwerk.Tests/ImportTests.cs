using Seitenwerk.Core.Import;
using Seitenwerk.Core.Modell;

namespace Seitenwerk.Tests;

public class ImportTests
{
    private static string Testdatei(string name) => Path.Combine(AppContext.BaseDirectory, "Testdaten", name);

    private static ImportErgebnis Lade() => OdgImporter.Importieren(Testdatei("lo-zeichnung.odg"));

    [Fact]
    public void Seitengroesse_UndSeitenanzahl()
    {
        var dok = Lade().Dokument;
        Assert.Equal(210, dok.SeitenBreite, 1);
        Assert.Equal(297, dok.SeitenHoehe, 1);
        Assert.Equal(2, dok.Seiten.Count);
        Assert.Equal("lo-zeichnung", dok.Titel);
    }

    [Fact]
    public void Textfeld_MitFormatierung()
    {
        var seite = Lade().Dokument.Seiten[0];
        var feld = Assert.IsType<TextfeldObjekt>(seite.Objekte[0]);
        Assert.Equal(10, feld.X, 1);
        Assert.Equal(10, feld.Y, 1);
        Assert.Equal(100, feld.Breite, 1);
        Assert.Equal(30, feld.Hoehe, 1);
        Assert.Null(feld.Fuellung);
        Assert.Null(feld.Rahmenfarbe);

        Assert.Equal(2, feld.Absaetze.Count);
        Assert.Equal("Hallo Welt", feld.Absaetze[0].Text);
        Assert.Equal("Zweite Zeile", feld.Absaetze[1].Text);
        Assert.Equal(Ausrichtung.Zentriert, feld.Absaetze[0].Ausrichtung);
        var lauf = feld.Absaetze[0].Laeufe[0];
        Assert.True(lauf.Fett);
        Assert.Equal("Arial", lauf.Schriftart);
        Assert.Equal(24, lauf.Groesse, 1);
        Assert.Equal("#FF0000", lauf.Farbe);
    }

    [Fact]
    public void Rechteck_MitFuellungRahmenUndText()
    {
        var form = Assert.IsType<FormObjekt>(Lade().Dokument.Seiten[0].Objekte[1]);
        Assert.Equal(FormArt.Rechteck, form.Form);
        Assert.Equal("#3366CC", form.Fuellung);
        Assert.Equal("#FF0000", form.Rahmenfarbe);
        Assert.Equal(1 * 72 / 25.4, form.Rahmenbreite, 1); // 0,1 cm = 1 mm in Punkt
        Assert.Equal("Text im Rechteck", form.Text);
        Assert.Equal(VertikaleAusrichtung.Mitte, form.VertikaleAusrichtung);
        Assert.Equal(10, form.X, 1);
        Assert.Equal(50, form.Y, 1);
    }

    [Fact]
    public void GedrehteEllipse_BehaeltMittelpunktUndWinkel()
    {
        var form = Assert.IsType<FormObjekt>(Lade().Dokument.Seiten[0].Objekte[2]);
        Assert.Equal(FormArt.Ellipse, form.Form);
        // In LibreOffice 30° gegen den Uhrzeigersinn = 330° im Uhrzeigersinn.
        Assert.Equal(330, form.Drehung, 1);
        Assert.Equal(120, form.MitteX, 1);
        Assert.Equal(65, form.MitteY, 1);
        Assert.Equal(60, form.Breite, 1);
        Assert.Equal(30, form.Hoehe, 1);
    }

    [Fact]
    public void Linie_MitStaerke()
    {
        var linie = Assert.IsType<LinieObjekt>(Lade().Dokument.Seiten[0].Objekte[3]);
        var (x1, y1, x2, y2) = linie.Punkte;
        Assert.Equal((10, 100, 90, 120), (Math.Round(x1), Math.Round(y1), Math.Round(x2), Math.Round(y2)));
        Assert.Equal(2 * 72 / 25.4, linie.Staerke, 1); // 0,2 cm = 2 mm in Punkt
    }

    [Fact]
    public void Polygon_WirdZumPfad()
    {
        var pfad = Assert.IsType<PfadObjekt>(Lade().Dokument.Seiten[0].Objekte[4]);
        Assert.Equal("#FFAA00", pfad.Fuellung);
        Assert.StartsWith("M 0 0 L 4000 0 L 2000 3000", pfad.Daten);
        Assert.EndsWith("Z", pfad.Daten);
        Assert.Equal(4000, pfad.ViewBreite);
        Assert.Equal(120, pfad.X, 1);
    }

    [Fact]
    public void Bild_WirdEingebettet()
    {
        var ergebnis = Lade();
        var bild = Assert.IsType<BildObjekt>(ergebnis.Dokument.Seiten[0].Objekte[5]);
        var medium = ergebnis.Dokument.FindeMedium(bild.MedienId);
        Assert.NotNull(medium);
        Assert.Equal("png", medium!.Endung);
        Assert.Equal("png", BildFormat.Erkennen(medium.Daten));
        Assert.Equal(40, bild.Breite, 1);
        Assert.Equal(140, bild.Y, 1);
    }

    [Fact]
    public void CustomShape_Diamant_WirdRaute()
    {
        var form = Assert.IsType<FormObjekt>(Lade().Dokument.Seiten[0].Objekte[6]);
        Assert.Equal(FormArt.Raute, form.Form);
        Assert.Equal("#CC00CC", form.Fuellung);
    }

    [Fact]
    public void Gruppe_WirdUebernommen()
    {
        var gruppe = Assert.IsType<GruppeObjekt>(Lade().Dokument.Seiten[0].Objekte[7]);
        Assert.Equal(2, gruppe.Kinder.Count);
        Assert.Equal(10, gruppe.X, 1);
        Assert.Equal(200, gruppe.Y, 1);
        Assert.Equal(50, gruppe.Breite, 1);
        Assert.Equal(30, gruppe.Hoehe, 1);
    }

    [Fact]
    public void GedrehtesTextfeld_Rotation90()
    {
        var feld = Assert.IsType<TextfeldObjekt>(Lade().Dokument.Seiten[0].Objekte[8]);
        Assert.Equal(270, feld.Drehung, 1);
        Assert.Equal(110, feld.MitteX, 1);
        Assert.Equal(210, feld.MitteY, 1);
        Assert.Equal(60, feld.Breite, 1);
        Assert.Equal("gedreht", feld.Text);
    }

    [Fact]
    public void ZweiteSeite_HatEigenesTextfeld()
    {
        var feld = Assert.IsType<TextfeldObjekt>(Lade().Dokument.Seiten[1].Objekte.Single());
        Assert.Equal("Seite 2", feld.Text);
    }

    [Fact]
    public void FlachesXml_WirdNichtAlsZipAkzeptiert()
    {
        Assert.Throws<ImportFehler>(() => OdgImporter.Importieren(Testdatei("lo-flach.fodg")));
    }

    [Fact]
    public void Laengen_WerdenInMillimeterUmgerechnet()
    {
        Assert.Equal(25.4, OdgLeser.Laenge("1in"));
        Assert.Equal(10, OdgLeser.Laenge("1cm"));
        Assert.Equal(12.5, OdgLeser.Laenge("12.5mm"));
        Assert.Null(OdgLeser.Laenge("abc"));
    }

    [Fact]
    public void BildFormat_LiestGroesseUndAufloesung()
    {
        var png = BildFormat.Groesse(File.ReadAllBytes(Testdatei("grafik.png")));
        Assert.NotNull(png);
        Assert.Equal((64, 48), (png!.Value.BreitePixel, png.Value.HoehePixel));
        Assert.Equal(300, png.Value.DpiX, 0);

        var jpg = BildFormat.Groesse(File.ReadAllBytes(Testdatei("foto.jpg")));
        Assert.NotNull(jpg);
        Assert.Equal((64, 48), (jpg!.Value.BreitePixel, jpg.Value.HoehePixel));
        Assert.Equal(150, jpg.Value.DpiX, 0);
    }

    [Fact]
    public async Task LibreOffice_WandeltFlachesOdgInZip()
    {
        var soffice = PubImport.FindeLibreOffice();
        if (soffice == null)
            return; // LibreOffice nicht installiert: Test entfällt

        var ordner = Path.Combine(Path.GetTempPath(), "swk-lo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ordner);
        try
        {
            var odg = await PubImport.NachOdgAsync(Testdatei("lo-flach.fodg"), soffice, ordner);
            var ergebnis = OdgImporter.Importieren(odg);
            Assert.Equal(2, ergebnis.Dokument.Seiten.Count);
            Assert.IsType<TextfeldObjekt>(ergebnis.Dokument.Seiten[0].Objekte[0]);
        }
        finally
        {
            Directory.Delete(ordner, recursive: true);
        }
    }
}
