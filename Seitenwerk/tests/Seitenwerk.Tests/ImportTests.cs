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

/// <summary>Tests mit echten Publisher-Dateien (Apache POI), die LibreOffice in Zeichnungen umgewandelt hat.</summary>
public class PublisherImportTests
{
    private static string Testdatei(string name) => Path.Combine(AppContext.BaseDirectory, "Testdaten", name);

    private static ImportErgebnis Lade(string name) => OdgImporter.Importieren(Testdatei(name), ausPublisher: true);

    private static IEnumerable<Objekt> Alle(Dokument d) => d.Seiten.SelectMany(s => s.Alle());

    [Fact]
    public void Tabelle_WirdInTextfelderUmgewandelt()
    {
        var ergebnis = Lade("publisher-sample.odg");
        var tabelle = ergebnis.Dokument.Seiten[1].Objekte.OfType<GruppeObjekt>().Single(g => g.Name == "Tabelle");
        var texte = tabelle.Kinder.OfType<TextfeldObjekt>().Select(t => t.Text).ToList();
        Assert.Equal(6, texte.Count);
        Assert.Contains("Table on page 2", texte);
        Assert.Contains("Bottom Right", texte);
        // Zellen liegen zeilenweise untereinander
        var zellen = tabelle.Kinder.OfType<TextfeldObjekt>().ToList();
        Assert.Equal(zellen[0].Y, zellen[1].Y, 3);
        Assert.True(zellen[2].Y > zellen[0].Y + 5);
        Assert.True(zellen[1].X > zellen[0].X + 40);
        Assert.Contains(ergebnis.Hinweise, h => h.Contains("Tabellen"));
    }

    [Fact]
    public void TabellenVorschaubild_WirdNichtAlsBildUebernommen()
    {
        var dok = Lade("publisher-sample.odg").Dokument;
        Assert.DoesNotContain(Alle(dok), o => o is BildObjekt);
        Assert.Empty(dok.Medien);
    }

    [Fact]
    public void BilderAlsFuellung_WerdenEingebettet()
    {
        var dok = Lade("publisher-brochure.odg").Dokument;
        var mitBild = Alle(dok).OfType<FlaechenObjekt>().Where(o => o.FuellungBildId != null).ToList();
        Assert.True(mitBild.Count >= 4, $"Füllbilder: {mitBild.Count}");
        foreach (var o in mitBild)
        {
            var medium = dok.FindeMedium(o.FuellungBildId!);
            Assert.NotNull(medium);
            Assert.Contains(medium!.Endung, new[] { "png", "jpg", "gif", "bmp", "tif", "emf", "wmf" });
            Assert.NotEmpty(medium.Daten);
        }
        // Zwei verschiedene Bildformate kommen in der Broschüre vor (Metadateien und Rasterbilder)
        Assert.True(dok.Medien.Select(m => m.Endung).Distinct().Count() >= 2);
    }

    [Fact]
    public void Broschuere_HatQuerformatUndZweiSeiten()
    {
        var dok = Lade("publisher-brochure.odg").Dokument;
        Assert.Equal(2, dok.Seiten.Count);
        Assert.Equal(297, dok.SeitenBreite, 0);
        Assert.Equal(210, dok.SeitenHoehe, 0);
        var schriften = Alle(dok).OfType<TextTraeger>().SelectMany(t => t.Absaetze).SelectMany(a => a.Laeufe).Select(l => l.Schriftart).Distinct().ToList();
        Assert.Contains("Gill Sans MT", schriften);
        Assert.Contains("Garamond", schriften);
    }

    [Fact]
    public void Textrahmen_BehaltenIhreUrspruenglicheGroesse()
    {
        // LibreOffice dehnt Rahmen bei langem Text bis über den Seitenrand; Publisher zeigt nur den Inhalt des Rahmens.
        var dok = Lade("publisher-brochure.odg").Dokument;
        foreach (var t in Alle(dok).OfType<TextfeldObjekt>())
            Assert.True(t.Y + t.Hoehe <= dok.SeitenHoehe + 1, $"Textfeld ragt über die Seite: y={t.Y}, h={t.Hoehe}");
    }

    [Fact]
    public void ObjekteLiegenAufDerSeite()
    {
        foreach (var name in new[] { "publisher-sample.odg", "publisher-brochure.odg", "publisher-bild.odg" })
        {
            var dok = Lade(name).Dokument;
            foreach (var seite in dok.Seiten)
                foreach (var o in seite.Objekte)
                {
                    Assert.True(o.Breite >= 0 && o.Hoehe >= 0, $"{name}: negative Größe");
                    Assert.True(o.X > -1 && o.Y > -1 && o.X < dok.SeitenBreite && o.Y < dok.SeitenHoehe, $"{name}: Objekt außerhalb der Seite ({o.X}, {o.Y})");
                }
        }
    }

    [Fact]
    public void ImportiertesDokument_UeberstehtSpeichernUndLaden()
    {
        var dok = Lade("publisher-brochure.odg").Dokument;
        using var ms = new MemoryStream();
        Seitenwerk.Core.Dateiformat.DokumentSpeicher.Speichern(dok, ms);
        ms.Position = 0;
        var geladen = Seitenwerk.Core.Dateiformat.DokumentSpeicher.Laden(ms);
        Assert.Equal(Alle(dok).Count(), Alle(geladen).Count());
        Assert.Equal(dok.Medien.Count, geladen.Medien.Count);
        Assert.Equal(
            Alle(dok).OfType<FlaechenObjekt>().Count(o => o.FuellungBildId != null),
            Alle(geladen).OfType<FlaechenObjekt>().Count(o => o.FuellungBildId != null));
        Assert.Equal(
            Alle(dok).OfType<FlaechenObjekt>().Select(o => (o.Fuellung, o.Rahmenfarbe)),
            Alle(geladen).OfType<FlaechenObjekt>().Select(o => (o.Fuellung, o.Rahmenfarbe)));
    }

    [Fact]
    public async Task EchteDatei_UeberLibreOffice_WirdGeoeffnet()
    {
        var soffice = PubImport.FindeLibreOffice();
        if (soffice == null && Environment.GetEnvironmentVariable("SEITENWERK_LO_ERFORDERLICH") == "1")
            Assert.Fail("LibreOffice wird für diesen Test benötigt, wurde aber nicht gefunden.");
        if (soffice == null)
            return; // ohne LibreOffice entfällt der Test

        foreach (var (datei, seiten) in new[] { ("publisher-sample.pub", 2), ("publisher-brochure.pub", 2) })
        {
            var ergebnis = await PubImport.ImportierenAsync(Testdatei(datei), soffice);
            Assert.Equal(seiten, ergebnis.Dokument.Seiten.Count);
            Assert.Equal(Path.GetFileNameWithoutExtension(datei), ergebnis.Dokument.Titel);
            Assert.NotEmpty(Alle(ergebnis.Dokument));
        }
    }
}
