using Seitenwerk.Core.Bearbeiten;
using Seitenwerk.Core.Dateiformat;
using Seitenwerk.Core.Modell;

namespace Seitenwerk.Tests;

public class ModellTests
{
    private static string Testdatei(string name) => Path.Combine(AppContext.BaseDirectory, "Testdaten", name);

    internal static Dokument Beispiel()
    {
        var dok = Dokument.Neu(210, 297);
        var seite = dok.Seiten[0];

        var text = TextfeldObjekt.Neu(10, 10, 100, 30);
        text.Fuellung = "#FFFFCC";
        text.Rahmenfarbe = "#000000";
        text.Absaetze = new List<Absatz>
        {
            new() { Ausrichtung = Ausrichtung.Zentriert, Laeufe = { new Textlauf("Hallo ") { Fett = true, Groesse = 20 }, new Textlauf("Welt") { Kursiv = true, Farbe = "#FF0000" } } },
            new("Zweiter Absatz"),
        };
        seite.Objekte.Add(text);

        seite.Objekte.Add(new FormObjekt { Form = FormArt.Ellipse, X = 20, Y = 60, Breite = 50, Hoehe = 30, Drehung = 45 });
        seite.Objekte.Add(LinieObjekt.Zwischen(10, 120, 100, 100));

        var medium = dok.MediumHinzufuegen(File.ReadAllBytes(Testdatei("foto.jpg")), "jpg");
        seite.Objekte.Add(new BildObjekt { MedienId = medium.Id, X = 100, Y = 100, Breite = 40, Hoehe = 30, Seitenverhaeltnis = 4.0 / 3 });

        seite.Objekte.Add(GruppeObjekt.Aus(new Objekt[]
        {
            new FormObjekt { X = 10, Y = 200, Breite = 20, Hoehe = 20 },
            new FormObjekt { X = 40, Y = 210, Breite = 20, Hoehe = 20 },
        }));
        seite.Objekte.Add(new PfadObjekt { X = 120, Y = 20, Breite = 30, Hoehe = 30, Daten = "M 0 0 L 10 0 L 5 10 Z", ViewBreite = 10, ViewHoehe = 10, Fuellung = "#00FF00" });

        dok.Seiten.Add(new Seite { Hintergrund = "#EEEEEE" });
        return dok;
    }

    [Fact]
    public void SpeichernUndLaden_BehaeltAllesBei()
    {
        var original = Beispiel();
        using var ms = new MemoryStream();
        DokumentSpeicher.Speichern(original, ms);
        ms.Position = 0;
        var geladen = DokumentSpeicher.Laden(ms);

        Assert.Equal(2, geladen.Seiten.Count);
        Assert.Equal("#EEEEEE", geladen.Seiten[1].Hintergrund);
        Assert.Equal(original.Seiten[0].Objekte.Select(o => o.GetType()), geladen.Seiten[0].Objekte.Select(o => o.GetType()));

        var text = Assert.IsType<TextfeldObjekt>(geladen.Seiten[0].Objekte[0]);
        Assert.Equal("#FFFFCC", text.Fuellung);
        Assert.Equal(2, text.Absaetze.Count);
        Assert.Equal(Ausrichtung.Zentriert, text.Absaetze[0].Ausrichtung);
        Assert.Equal("Hallo Welt", text.Absaetze[0].Text);
        Assert.True(text.Absaetze[0].Laeufe[0].Fett);
        Assert.Equal(20, text.Absaetze[0].Laeufe[0].Groesse);
        Assert.Equal("#FF0000", text.Absaetze[0].Laeufe[1].Farbe);

        var form = Assert.IsType<FormObjekt>(geladen.Seiten[0].Objekte[1]);
        Assert.Equal(FormArt.Ellipse, form.Form);
        Assert.Equal(45, form.Drehung);

        var bild = Assert.IsType<BildObjekt>(geladen.Seiten[0].Objekte[3]);
        var medium = geladen.FindeMedium(bild.MedienId);
        Assert.NotNull(medium);
        Assert.Equal(File.ReadAllBytes(Testdatei("foto.jpg")), medium!.Daten);

        var gruppe = Assert.IsType<GruppeObjekt>(geladen.Seiten[0].Objekte[4]);
        Assert.Equal(2, gruppe.Kinder.Count);
        Assert.IsType<PfadObjekt>(geladen.Seiten[0].Objekte[5]);
    }

    [Fact]
    public void KeineFuellungUndKeinRahmen_UeberlebenSpeichernUndMomentaufnahme()
    {
        var dok = Dokument.Neu();
        // FormObjekt hat standardmäßig Füllung und Rahmen; null bedeutet bewusst "keine".
        dok.Seiten[0].Objekte.Add(new FormObjekt { Breite = 10, Hoehe = 10, Fuellung = null, Rahmenfarbe = null });
        dok.Seiten[0].Objekte.Add(new TextfeldObjekt { Breite = 10, Hoehe = 10, Fuellung = null, Rahmenfarbe = null });

        using var ms = new MemoryStream();
        DokumentSpeicher.Speichern(dok, ms);
        ms.Position = 0;
        var geladen = DokumentSpeicher.Laden(ms);
        var snapshot = DokumentSpeicher.AusMomentaufnahme(DokumentSpeicher.Momentaufnahme(dok), dok.Medien);

        foreach (var d in new[] { geladen, snapshot })
            foreach (var o in d.Seiten[0].Objekte.OfType<FlaechenObjekt>())
            {
                Assert.Null(o.Fuellung);
                Assert.Null(o.Rahmenfarbe);
            }
    }

    [Fact]
    public void Speichern_EntferntNichtVerwendeteBilder()
    {
        var dok = Beispiel();
        dok.MediumHinzufuegen(new byte[] { 1, 2, 3 }, "png");
        using var ms = new MemoryStream();
        DokumentSpeicher.Speichern(dok, ms);
        ms.Position = 0;
        Assert.Single(DokumentSpeicher.Laden(ms).Medien);
    }

    [Fact]
    public void Speichern_UeberschreibtDateiAtomar()
    {
        var pfad = Path.Combine(Path.GetTempPath(), "swk-test-" + Guid.NewGuid().ToString("N") + ".swk");
        try
        {
            DokumentSpeicher.Speichern(Beispiel(), pfad);
            DokumentSpeicher.Speichern(Dokument.Neu(100, 100), pfad);
            var geladen = DokumentSpeicher.Laden(pfad);
            Assert.Equal(100, geladen.SeitenBreite);
            Assert.False(File.Exists(pfad + ".tmp"));
        }
        finally
        {
            File.Delete(pfad);
        }
    }

    [Fact]
    public void Laden_MitFalscherDatei_WirftDateiFehler()
    {
        using var ms = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        Assert.Throws<DateiFehler>(() => DokumentSpeicher.Laden(ms));
    }

    [Fact]
    public void Momentaufnahme_StelltDokumentMitBildernWiederHer()
    {
        var dok = Beispiel();
        var json = DokumentSpeicher.Momentaufnahme(dok);
        var wieder = DokumentSpeicher.AusMomentaufnahme(json, dok.Medien);
        Assert.Equal(json, DokumentSpeicher.Momentaufnahme(wieder));
        Assert.NotEmpty(wieder.Medien[0].Daten);
    }

    [Fact]
    public void Kopie_VergibtNeueIdsAuchInGruppen()
    {
        var gruppe = GruppeObjekt.Aus(new Objekt[] { new FormObjekt { Breite = 5, Hoehe = 5 }, new FormObjekt { X = 10, Breite = 5, Hoehe = 5 } });
        var kopie = (GruppeObjekt)gruppe.Kopie();
        Assert.NotEqual(gruppe.Id, kopie.Id);
        Assert.All(kopie.Kinder, k => Assert.DoesNotContain(gruppe.Kinder, g => g.Id == k.Id));
    }

    [Fact]
    public void Linie_MerktSichAnfangUndEnde()
    {
        var l = LinieObjekt.Zwischen(100, 50, 10, 80); // von rechts oben nach links unten
        var (x1, y1, x2, y2) = l.Punkte;
        Assert.Equal((100, 50, 10, 80), (x1, y1, x2, y2));
    }

    [Fact]
    public void UndoVerlauf_FunktioniertWieErwartet()
    {
        var v = new UndoVerlauf();
        v.Zuruecksetzen("a");
        Assert.False(v.KannRueckgaengig);
        Assert.True(v.Merke("b"));
        Assert.False(v.Merke("b"));
        v.Merke("c");
        Assert.Equal("b", v.Rueckgaengig());
        Assert.Equal("a", v.Rueckgaengig());
        Assert.Null(v.Rueckgaengig());
        Assert.Equal("b", v.Wiederholen());
        v.Merke("x"); // verwirft "c"
        Assert.False(v.KannWiederholen);
        Assert.Equal("b", v.Rueckgaengig());
    }

    [Fact]
    public void UndoVerlauf_BegrenztDieAnzahl()
    {
        var v = new UndoVerlauf(5);
        v.Zuruecksetzen("0");
        for (var i = 1; i <= 20; i++)
            v.Merke(i.ToString());
        var schritte = 0;
        while (v.Rueckgaengig() != null)
            schritte++;
        Assert.Equal(4, schritte);
    }
}

public class GeometrieTests
{
    [Fact]
    public void Trifft_BerücksichtigtDrehung()
    {
        // 100 x 10 mm Balken, um 90° gedreht: steht danach senkrecht um die Mitte (50, 5).
        var balken = new FormObjekt { X = 0, Y = 0, Breite = 100, Hoehe = 10, Drehung = 90 };
        Assert.True(Geometrie.Trifft(balken, 50, 40, 0));
        Assert.False(Geometrie.Trifft(balken, 90, 5, 0));
        Assert.False(Geometrie.Trifft(balken, 50, 70, 0));
    }

    [Fact]
    public void Trifft_Ellipse_NichtInDenEcken()
    {
        var e = new FormObjekt { Form = FormArt.Ellipse, X = 0, Y = 0, Breite = 100, Hoehe = 100 };
        Assert.True(Geometrie.Trifft(e, 50, 50, 0));
        Assert.False(Geometrie.Trifft(e, 3, 3, 0));
    }

    [Fact]
    public void Trifft_Linie_MitToleranz()
    {
        var l = LinieObjekt.Zwischen(0, 0, 100, 0);
        Assert.True(Geometrie.Trifft(l, 50, 1.5, 2));
        Assert.False(Geometrie.Trifft(l, 50, 10, 2));
    }

    [Fact]
    public void UmgebendesRechteck_GedrehtesObjekt()
    {
        var o = new FormObjekt { X = 0, Y = 0, Breite = 100, Hoehe = 20, Drehung = 90 };
        var (x0, y0, x1, y1) = Geometrie.UmgebendesRechteck(o);
        Assert.Equal(40, x0, 6);
        Assert.Equal(60, x1, 6);
        Assert.Equal(-40, y0, 6);
        Assert.Equal(60, y1, 6);
    }

    [Theory]
    [InlineData(FormArt.Dreieck, 3)]
    [InlineData(FormArt.Raute, 4)]
    [InlineData(FormArt.Fuenfeck, 5)]
    [InlineData(FormArt.Sechseck, 6)]
    [InlineData(FormArt.Stern, 10)]
    [InlineData(FormArt.Pfeil, 7)]
    public void Polygone_LiegenImEinheitsquadrat(FormArt form, int ecken)
    {
        var p = Geometrie.Polygon(form)!;
        Assert.Equal(ecken, p.Length);
        Assert.All(p, punkt =>
        {
            Assert.InRange(punkt.X, -1e-9, 1 + 1e-9);
            Assert.InRange(punkt.Y, -1e-9, 1 + 1e-9);
        });
    }

    [Fact]
    public void Rechteck_HatKeinPolygon() => Assert.Null(Geometrie.Polygon(FormArt.Rechteck));
}

public class AnordnungTests
{
    private static FormObjekt F(double x, double y, double b = 10, double h = 10) => new() { X = x, Y = y, Breite = b, Hoehe = h };

    [Fact]
    public void Reihenfolge_Operationen()
    {
        var a = F(0, 0); var b = F(0, 0); var c = F(0, 0); var d = F(0, 0);
        var seite = new Seite { Objekte = { a, b, c, d } };

        Anordnung.InDenVordergrund(seite, new[] { a });
        Assert.Equal(new Objekt[] { b, c, d, a }, seite.Objekte);

        Anordnung.InDenHintergrund(seite, new[] { d });
        Assert.Equal(new Objekt[] { d, b, c, a }, seite.Objekte);

        Anordnung.EineEbeneNachVorn(seite, new[] { d });
        Assert.Equal(new Objekt[] { b, d, c, a }, seite.Objekte);

        Anordnung.EineEbeneNachHinten(seite, new[] { a });
        Assert.Equal(new Objekt[] { b, d, a, c }, seite.Objekte);

        // Das oberste Objekt kann nicht weiter nach vorn.
        Anordnung.EineEbeneNachVorn(seite, new[] { c });
        Assert.Equal(new Objekt[] { b, d, a, c }, seite.Objekte);
    }

    [Fact]
    public void Ausrichten_AnSeiteUndAuswahl()
    {
        var a = F(5, 5, 20, 10);
        var b = F(50, 40, 10, 10);
        Anordnung.Ausrichten(new Objekt[] { a, b }, AusrichtenArt.Rechts, (0, 0, 100, 100));
        Assert.Equal(80, a.X);
        Assert.Equal(90, b.X);

        Anordnung.Ausrichten(new Objekt[] { a, b }, AusrichtenArt.Mitte, (0, 0, 100, 100));
        Assert.Equal(45, a.Y);
        Assert.Equal(45, b.Y);
    }

    [Fact]
    public void Verteilen_GleichmaessigeAbstaende()
    {
        var a = F(0, 0); var b = F(15, 0); var c = F(90, 0);
        Anordnung.Verteilen(new Objekt[] { c, a, b }, waagerecht: true);
        Assert.Equal(0, a.X);
        Assert.Equal(45, b.X);
        Assert.Equal(90, c.X);
    }

    [Fact]
    public void GruppierenUndAufloesen()
    {
        var a = F(0, 0); var b = F(30, 30); var c = F(60, 0); var d = F(80, 80);
        var seite = new Seite { Objekte = { a, b, c, d } };

        var gruppe = Anordnung.Gruppieren(seite, new Objekt[] { a, c });
        Assert.NotNull(gruppe);
        Assert.Equal(3, seite.Objekte.Count);
        Assert.Equal(0, gruppe!.X);
        Assert.Equal(70, gruppe.Breite);

        // Verschieben der Gruppe verschiebt die Kinder mit.
        gruppe.Verschieben(5, 5);
        Assert.Equal(5, a.X);
        Assert.Equal(65, c.X);

        var kinder = Anordnung.Aufloesen(seite, gruppe);
        Assert.Equal(2, kinder.Count);
        Assert.Equal(4, seite.Objekte.Count);
        Assert.DoesNotContain(seite.Objekte, o => o is GruppeObjekt);
    }

    [Fact]
    public void Gruppieren_EinzelnesObjektIstNichtMoeglich()
    {
        var a = F(0, 0);
        Assert.Null(Anordnung.Gruppieren(new Seite { Objekte = { a } }, new Objekt[] { a }));
    }

    [Fact]
    public void Gruppe_SkalierenSkaliertKinder()
    {
        var a = F(0, 0, 10, 10); var b = F(10, 10, 10, 10);
        var g = GruppeObjekt.Aus(new Objekt[] { a, b });
        g.Skalieren(2, 2, g.X, g.Y);
        Assert.Equal(40, g.Breite);
        Assert.Equal(20, a.Breite);
        Assert.Equal(20, b.X);
    }

    [Fact]
    public void Drehen_NormiertDenWinkel()
    {
        var o = F(0, 0);
        Anordnung.Drehen(o, 90);
        Anordnung.Drehen(o, 90);
        Anordnung.Drehen(o, 90);
        Anordnung.Drehen(o, 90);
        Assert.Equal(0, o.Drehung);
        Anordnung.Drehen(o, -90);
        Assert.Equal(270, o.Drehung);
    }
}
