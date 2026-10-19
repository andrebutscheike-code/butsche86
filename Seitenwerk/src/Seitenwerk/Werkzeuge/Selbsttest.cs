using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Seitenwerk.Core.Bearbeiten;
using Seitenwerk.Core.Dateiformat;
using Seitenwerk.Core.Modell;
using Seitenwerk.Darstellung;
using Seitenwerk.Dialoge;
using Seitenwerk.Steuerung;

namespace Seitenwerk.Werkzeuge;

/// <summary>
/// Startet das Programm ohne Benutzer, baut ein Beispieldokument, führt Befehle aus und rendert Seiten. Aufruf: Seitenwerk.exe --selbsttest ORDNER.
/// Das Ergebnis steht in ORDNER\selbsttest.log, der Exitcode ist 0 bei Erfolg.
/// </summary>
internal static class Selbsttest
{
    private static string _logDatei = "";
    private static int _fehler;

    private static void Zeile(string text)
    {
        // Sofort in die Datei schreiben, damit auch bei einem Absturz sichtbar bleibt, wie weit der Test kam.
        if (_logDatei.Length > 0)
        {
            try
            {
                File.AppendAllText(_logDatei, text + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void Pruefe(bool bedingung, string beschreibung)
    {
        if (bedingung)
        {
            Zeile("OK      " + beschreibung);
        }
        else
        {
            Zeile("FEHLER  " + beschreibung);
            _fehler++;
        }
    }

    public static void MeldeFehler(Exception ex)
    {
        Zeile("AUSNAHME " + ex);
        _fehler++;
    }

    public static void Starten(Application app, string ausgabe)
    {
        Directory.CreateDirectory(ausgabe);
        _logDatei = Path.Combine(ausgabe, "selbsttest.log");
        File.WriteAllText(_logDatei, "Selbsttest gestartet" + Environment.NewLine, Encoding.UTF8);
        // Die Anwendung wird erst am Ende ausdrücklich beendet. Würde das letzte Fenster schon vorher schließen,
        // käme der Abschluss (Protokoll, Exitcode) nicht mehr zur Ausführung.
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
        {
            try
            {
                await Ablauf(app, ausgabe);
            }
            catch (Exception ex)
            {
                MeldeFehler(ex);
            }
            finally
            {
                Zeile(_fehler == 0 ? "ERGEBNIS: alle Prüfungen bestanden" : $"ERGEBNIS: {_fehler} Fehler");
                app.Shutdown(_fehler == 0 ? 0 : 1);
            }
        });
    }

    private static async Task Ruhe(Application app)
    {
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static byte[] TestBild(bool png)
    {
        const int b = 96, h = 64;
        var pixel = new byte[b * h * 3];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < b; x++)
            {
                var i = (y * b + x) * 3;
                pixel[i] = (byte)(x * 255 / b);
                pixel[i + 1] = (byte)(y * 255 / h);
                pixel[i + 2] = 160;
            }
        var quelle = BitmapSource.Create(b, h, 96, 96, PixelFormats.Bgr24, null, pixel, b * 3);
        BitmapEncoder encoder = png ? new PngBitmapEncoder() : new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(quelle));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static Dokument Beispiel()
    {
        var dok = Dokument.Neu(210, 297);
        var seite = dok.Seiten[0];

        var kopf = new FormObjekt { Form = FormArt.Rechteck, X = 0, Y = 0, Breite = 210, Hoehe = 40, Fuellung = "#1F4E79", Rahmenfarbe = null };
        kopf.TextSetzen("Sommerfest 2026", new Textlauf { Schriftart = "Segoe UI", Groesse = 36, Fett = true, Farbe = "#FFFFFF" });
        seite.Objekte.Add(kopf);

        var text = TextfeldObjekt.Neu(15, 55, 110, 60);
        text.Absaetze = new List<Absatz>
        {
            new() { Laeufe = { new Textlauf("Liebe Nachbarn, ") { Groesse = 14 }, new Textlauf("wir laden herzlich ein") { Groesse = 14, Fett = true, Farbe = "#C00000" } } },
            new("Samstag, 18. Juli, ab 15 Uhr im Gemeindegarten mit Kuchen, Musik und Spielen für Kinder.") { Zeilenabstand = 1.15 },
            new("Ein langer Text, der umbrochen werden muss, damit auch die Zeilenumbrüche und die Blocksatzausrichtung geprüft werden können.", Ausrichtung.Blocksatz),
        };
        seite.Objekte.Add(text);

        seite.Objekte.Add(new FormObjekt { Form = FormArt.Ellipse, X = 140, Y = 55, Breite = 55, Hoehe = 40, Fuellung = "#FFC000", Rahmenfarbe = "#7F6000", Rahmenbreite = 2 });
        seite.Objekte.Add(new FormObjekt { Form = FormArt.Stern, X = 150, Y = 100, Breite = 35, Hoehe = 35, Fuellung = "#ED7D31", Drehung = 15 });
        seite.Objekte.Add(new FormObjekt { Form = FormArt.AbgerundetesRechteck, X = 15, Y = 125, Breite = 80, Hoehe = 25, Fuellung = "#E2EFDA", Rahmenfarbe = "#548235", Rahmenbreite = 1.5, Eckenradius = 6 });
        seite.Objekte.Add(LinieObjekt.Zwischen(15, 160, 100, 175).Mit(l => { l.Ende = Pfeilspitze.Pfeil; l.Farbe = "#C00000"; l.Staerke = 3; }));

        var png = dok.MediumHinzufuegen(TestBild(true), "png");
        seite.Objekte.Add(new BildObjekt { MedienId = png.Id, X = 15, Y = 185, Breite = 72, Hoehe = 48, Seitenverhaeltnis = 1.5, Rahmenfarbe = "#404040", Rahmenbreite = 1 });
        var jpg = dok.MediumHinzufuegen(TestBild(false), "jpg");
        seite.Objekte.Add(new BildObjekt { MedienId = jpg.Id, X = 100, Y = 185, Breite = 72, Hoehe = 48, Seitenverhaeltnis = 1.5, Drehung = 8, BeschnittLinks = 0.1 });

        seite.Objekte.Add(new PfadObjekt { X = 150, Y = 150, Breite = 45, Hoehe = 30, Daten = "M 0 0 L 10 0 L 5 10 Z", ViewBreite = 10, ViewHoehe = 10, Fuellung = "#9DC3E6", Rahmenfarbe = "#1F4E79" });

        var seite2 = new Seite { Hintergrund = "#FFF2CC" };
        var t2 = TextfeldObjekt.Neu(20, 20, 100, 30);
        t2.TextSetzen("Seite 2");
        seite2.Objekte.Add(t2);
        dok.Seiten.Add(seite2);
        return dok;
    }

    private static T Mit<T>(this T objekt, Action<T> aktion)
    {
        aktion(objekt);
        return objekt;
    }

    private static int NichtWeisse(BitmapSource bild)
    {
        var quelle = new FormatConvertedBitmap(bild, PixelFormats.Bgra32, null, 0);
        var stride = quelle.PixelWidth * 4;
        var daten = new byte[stride * quelle.PixelHeight];
        quelle.CopyPixels(daten, stride, 0);
        var anzahl = 0;
        for (var i = 0; i < daten.Length; i += 4)
        {
            if (daten[i] < 245 || daten[i + 1] < 245 || daten[i + 2] < 245)
                anzahl++;
        }
        return anzahl;
    }

    /// <summary>Grobe Textdarstellung eines Bilds, damit das Aussehen im Protokoll beurteilt werden kann.</summary>
    private static string Ascii(BitmapSource bild, int spalten, int zeilen)
    {
        var grau = new FormatConvertedBitmap(bild, PixelFormats.Gray8, null, 0);
        var b = grau.PixelWidth;
        var h = grau.PixelHeight;
        var daten = new byte[b * h];
        grau.CopyPixels(daten, b, 0);
        const string zeichen = " .:-=+*#%@";
        var sb = new StringBuilder();
        for (var z = 0; z < zeilen; z++)
        {
            for (var sp = 0; sp < spalten; sp++)
            {
                int x0 = sp * b / spalten, x1 = Math.Max(x0 + 1, (sp + 1) * b / spalten);
                int y0 = z * h / zeilen, y1 = Math.Max(y0 + 1, (z + 1) * h / zeilen);
                long summe = 0;
                var anzahl = 0;
                for (var y = y0; y < y1 && y < h; y++)
                    for (var x = x0; x < x1 && x < b; x++)
                    {
                        summe += daten[y * b + x];
                        anzahl++;
                    }
                var hell = anzahl == 0 ? 255 : (int)(summe / anzahl);
                sb.Append(hell >= 248 ? ' ' : zeichen[Math.Clamp((255 - hell) * zeichen.Length / 256, 1, zeichen.Length - 1)]);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static async Task Ablauf(Application app, string ausgabe)
    {
        Zeile($"Selbsttest gestartet, Ausgabe: {ausgabe}");

        // ------------------------------------------------ Hauptfenster aufbauen
        var dokument = Beispiel();
        var editor = new EditorSitzung(dokument);
        var fenster = new MainWindow(editor, null);
        fenster.Show();
        await Ruhe(app);

        Pruefe(fenster.IsLoaded, "Hauptfenster geladen");
        Pruefe(fenster.Ansichten.Count == 2, $"Seitenansichten aufgebaut ({fenster.Ansichten.Count})");
        Pruefe(fenster.Menueband.Items.Count >= 6, $"Menüband mit {fenster.Menueband.Items.Count} Registerkarten");
        var nichtZugeordnet = fenster.NichtZugeordneteBefehle();
        Pruefe(nichtZugeordnet.Count == 0, "Alle Menüband-Befehle sind belegt" + (nichtZugeordnet.Count > 0 ? ": " + string.Join(", ", nichtZugeordnet) : ""));
        Pruefe(fenster.Title.EndsWith("Seitenwerk"), $"Fenstertitel: {fenster.Title}");

        // Bildschirmfoto des Fensters
        var fensterBild = new RenderTargetBitmap((int)fenster.ActualWidth, (int)fenster.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        fensterBild.Render(fenster);
        File.WriteAllBytes(Path.Combine(ausgabe, "fenster.png"), Exporter.AlsPng(fensterBild));
        Pruefe(NichtWeisse(fensterBild) > 20000, "Fenster enthält sichtbare Oberfläche");
        Zeile("Fenster (grob):");
        Zeile(Ascii(fensterBild, 150, 52));

        // ------------------------------------------------ Rendern
        var seite1 = Exporter.RenderSeite(dokument, dokument.Seiten[0], 100);
        File.WriteAllBytes(Path.Combine(ausgabe, "seite1.png"), Exporter.AlsPng(seite1));
        var gefuellt = NichtWeisse(seite1);
        Pruefe(gefuellt > seite1.PixelWidth * seite1.PixelHeight / 10, $"Seite 1 gerendert ({seite1.PixelWidth}×{seite1.PixelHeight}, {gefuellt} farbige Pixel)");
        var seite2 = Exporter.RenderSeite(dokument, dokument.Seiten[1], 50);
        Pruefe(seite2.PixelWidth > 100, "Seite 2 gerendert");
        Zeile("Seite 1 (grob):");
        Zeile(Ascii(seite1, 90, 60));
        File.WriteAllBytes(Path.Combine(ausgabe, "seite2.png"), Exporter.AlsPng(seite2));

        var vorschau = Exporter.Vorschau(dokument, dokument.Seiten[0], 240);
        Pruefe(vorschau.PixelWidth is >= 230 and <= 250, $"Vorschaubild {vorschau.PixelWidth}×{vorschau.PixelHeight}");

        var pdf = Path.Combine(ausgabe, "test.pdf");
        Exporter.PdfSpeichern(dokument, pdf, 150);
        var pdfBytes = File.ReadAllBytes(pdf);
        Pruefe(pdfBytes.Length > 5000 && Encoding.ASCII.GetString(pdfBytes, 0, 5) == "%PDF-", $"PDF geschrieben ({pdfBytes.Length} Bytes)");

        var pngDateien = Exporter.PngSpeichern(dokument, ausgabe, "export");
        Pruefe(pngDateien.Count == 2 && pngDateien.All(File.Exists), "PNG-Export für alle Seiten");

        var paginator = new DokumentPaginator(dokument, new Size(793, 1122), 0, 1);
        Pruefe(paginator.PageCount == 2 && paginator.GetPage(1).Visual != null, "Druckseiten erzeugt");

        // ------------------------------------------------ Speichern und Laden
        var swk = Path.Combine(ausgabe, "test.swk");
        DokumentSpeicher.Speichern(dokument, swk);
        var geladen = DokumentSpeicher.Laden(swk);
        Pruefe(geladen.Seiten.Count == 2 && geladen.Seiten[0].Objekte.Count == dokument.Seiten[0].Objekte.Count && geladen.Medien.Count == 2,
            "Speichern und Laden (.swk)");
        var wieder = Exporter.RenderSeite(geladen, geladen.Seiten[0], 100);
        Pruefe(NichtWeisse(wieder) == gefuellt, "Geladenes Dokument sieht identisch aus");

        // ------------------------------------------------ Bearbeiten
        var seite = editor.Dokument.Seiten[0];
        var ausgangsAnzahl = seite.Objekte.Count;
        var neu = new FormObjekt { Form = FormArt.Raute, X = 20, Y = 245, Breite = 30, Hoehe = 30 };
        editor.ObjektHinzufuegen(seite, neu);
        Pruefe(seite.Objekte.Count == ausgangsAnzahl + 1 && editor.Auswahl.Count == 1 && editor.Geaendert, "Objekt eingefügt und ausgewählt");

        editor.Fuellung("#00B050");
        Pruefe(neu.Fuellung == "#00B050", "Füllfarbe gesetzt");

        editor.Rueckgaengig();
        var nachUndo = editor.Dokument.Seiten[0];
        Pruefe(nachUndo.Objekte.Count == ausgangsAnzahl + 1 && ((FormObjekt)nachUndo.Objekte[^1]).Fuellung != "#00B050", "Rückgängig macht Füllfarbe rückgängig");
        editor.Rueckgaengig();
        Pruefe(editor.Dokument.Seiten[0].Objekte.Count == ausgangsAnzahl, "Rückgängig entfernt eingefügtes Objekt");
        editor.Wiederholen();
        editor.Wiederholen();
        Pruefe(editor.Dokument.Seiten[0].Objekte.Count == ausgangsAnzahl + 1 && ((FormObjekt)editor.Dokument.Seiten[0].Objekte[^1]).Fuellung == "#00B050", "Wiederholen stellt beides wieder her");
        await Ruhe(app);
        Pruefe(fenster.Ansichten.Count == 2, "Ansichten nach Rückgängig/Wiederholen intakt");

        // Ausrichten, Gruppieren, Reihenfolge über die Menübefehle
        seite = editor.Dokument.Seiten[0];
        editor.AuswahlSetzen(seite.Objekte.TakeLast(2));
        fenster.FuehreAus("Anordnen.Gruppieren");
        Pruefe(editor.Dokument.Seiten[0].Objekte[^1] is GruppeObjekt, "Gruppieren über Befehl");
        fenster.FuehreAus("Ausrichten.Links");
        fenster.FuehreAus("Drehen.Rechts");
        fenster.FuehreAus("Anordnen.Hintergrund");
        Pruefe(editor.Dokument.Seiten[0].Objekte[0] is GruppeObjekt, "In den Hintergrund");
        fenster.FuehreAus("Anordnen.Aufheben");
        Pruefe(!editor.Dokument.Seiten[0].Objekte.Any(o => o is GruppeObjekt), "Gruppierung aufheben");
        fenster.FuehreAus("Bearbeiten.Duplizieren");
        fenster.FuehreAus("Bearbeiten.Loeschen");
        fenster.FuehreAus("Linie.Staerke.2");
        fenster.FuehreAus("Linie.Art.Gestrichelt");
        fenster.FuehreAus("Raender.20");
        fenster.FuehreAus("Raender.10");
        fenster.FuehreAus("Seite.Quer");
        Pruefe(editor.Dokument.SeitenBreite > editor.Dokument.SeitenHoehe, "Querformat");
        fenster.FuehreAus("Seite.Hoch");
        Pruefe(editor.Dokument.SeitenBreite < editor.Dokument.SeitenHoehe, "Hochformat");

        // Seiten
        fenster.FuehreAus("Seite.Neu");
        Pruefe(editor.Dokument.Seiten.Count == 3, "Seite eingefügt");
        fenster.FuehreAus("Seite.Duplizieren");
        Pruefe(editor.Dokument.Seiten.Count == 4, "Seite dupliziert");
        fenster.FuehreAus("Seite.Vor");
        Pruefe(editor.SeiteLoeschen(editor.AktuelleSeite) && editor.Dokument.Seiten.Count == 3, "Seite gelöscht");
        await Ruhe(app);
        Pruefe(fenster.Ansichten.Count == 3, "Ansichten folgen der Seitenzahl");
        editor.AktuelleSeite = 0;

        // Zoom
        foreach (var befehl in new[] { "Zoom.Plus", "Zoom.Minus", "Zoom.100", "Zoom.GanzeSeite", "Zoom.Seitenbreite" })
            fenster.FuehreAus(befehl);
        await Ruhe(app);
        Pruefe(editor.Zoom is > 0.1 and < 8, $"Zoom-Befehle ({editor.Zoom:0.00})");

        // ------------------------------------------------ Textbearbeitung
        var ansicht = fenster.Ansichten[0];
        var textfeld = editor.Dokument.Seiten[0].Objekte.OfType<TextfeldObjekt>().First();
        editor.AuswahlSetzen(new[] { textfeld });
        ansicht.BeginneTextBearbeitung(textfeld);
        await Ruhe(app);
        Pruefe(editor.Textbearbeitung != null, "Textbearbeitung gestartet");
        if (editor.Textbearbeitung is RichTextBearbeitung bearbeitung)
        {
            bearbeitung.Box.AppendText(" – Ergänzung");
            bearbeitung.Box.SelectAll();
            editor.TextFormatAnwenden(new TextFormat { Kursiv = true, Farbe = "#0070C0", Groesse = 16 });
            var zustand = editor.LeseTextFormat();
            Pruefe(zustand is { Kursiv: true, Groesse: 16 }, "Format in der Auswahl lesbar");
            bearbeitung.Beenden(true);
        }
        await Ruhe(app);
        Pruefe(editor.Textbearbeitung == null, "Textbearbeitung beendet");
        var neuesTextfeld = editor.Dokument.Seiten[0].Objekte.OfType<TextfeldObjekt>().First();
        Pruefe(neuesTextfeld.Text.Contains("Ergänzung"), "Text aus dem Editor übernommen");
        Pruefe(neuesTextfeld.Absaetze.SelectMany(a => a.Laeufe).All(l => l.Kursiv && l.Farbe.Equals("#0070C0", StringComparison.OrdinalIgnoreCase)),
            "Formatierung aus dem Editor übernommen");
        Pruefe(neuesTextfeld.Absaetze.Count == 3, $"Absatzanzahl bleibt erhalten ({neuesTextfeld.Absaetze.Count})");

        // Format auf ganzes Objekt ohne Textbearbeitung
        editor.AuswahlSetzen(new[] { neuesTextfeld });
        editor.TextFormatAnwenden(new TextFormat { Fett = true, Ausrichtung = Ausrichtung.Zentriert, Schriftart = "Arial" });
        Pruefe(neuesTextfeld.Absaetze.All(a => a.Ausrichtung == Ausrichtung.Zentriert && a.Laeufe.All(l => l.Fett && l.Schriftart == "Arial")), "Textformat auf ganzes Textfeld");

        // Zwischenablage
        editor.AuswahlSetzen(new[] { neuesTextfeld });
        var vorher = editor.Dokument.Seiten[0].Objekte.Count;
        try
        {
            editor.Kopieren();
            editor.Einfuegen();
            // Die Zwischenablage hängt vom Desktop der Testumgebung ab; ein Ausbleiben gilt deshalb nur als Hinweis.
            Zeile(editor.Dokument.Seiten[0].Objekte.Count == vorher + 1
                ? "OK      Kopieren und Einfügen (Zwischenablage)"
                : "HINWEIS  Einfügen aus der Zwischenablage hat kein Objekt ergeben");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.Threading.ThreadStateException)
        {
            Zeile("HINWEIS  Zwischenablage nicht verfügbar: " + ex.Message);
        }

        // ------------------------------------------------ Dialoge und Symbole
        var d1 = new SeiteEinrichtenDialog("Test", 210, 297, new Seitenraender());
        var d2 = new FarbDialog("#FF8800");
        var d3 = new FortschrittFenster("Test");
        Pruefe(d1 != null && d2.Farbe == "#FF8800" && d3 != null, "Dialoge lassen sich erzeugen");

        var symbolFehler = 0;
        foreach (var feld in typeof(Symbole).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name != nameof(Symbole.SymbolSchrift)))
        {
            try
            {
                Symbole.Skizze((string)feld.GetRawConstantValue()!);
            }
            catch (Exception ex)
            {
                symbolFehler++;
                Zeile($"  Symbol {feld.Name}: {ex.Message}");
            }
        }
        foreach (var art in Enum.GetValues<FormArt>())
            Symbole.Form(art);
        Pruefe(symbolFehler == 0, "Alle Symbole gültig");

        // ------------------------------------------------ Seiten ohne Fehler zeichnen
        foreach (var a in fenster.Ansichten)
            a.Aktualisieren();
        editor.ZeigeRaster = true;
        editor.ZeigeRaender = true;
        await Ruhe(app);
        var mitHilfen = new RenderTargetBitmap(600, 800, 96, 96, PixelFormats.Pbgra32);
        mitHilfen.Render(fenster.Ansichten[0]);
        Pruefe(NichtWeisse(mitHilfen) > 1000, "Seitenansicht mit Hilfen gerendert");
        File.WriteAllBytes(Path.Combine(ausgabe, "ansicht.png"), Exporter.AlsPng(mitHilfen));

        var endBild = new RenderTargetBitmap((int)fenster.ActualWidth, (int)fenster.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        endBild.Render(fenster);
        File.WriteAllBytes(Path.Combine(ausgabe, "fenster-ende.png"), Exporter.AlsPng(endBild));

        // Änderungen verwerfen, damit das Schließen nicht nachfragt
        editor.NeuesDokument(Dokument.Neu(), null, false);
    }
}
