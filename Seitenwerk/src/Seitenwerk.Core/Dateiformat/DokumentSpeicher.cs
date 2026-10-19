using System.IO.Compression;
using System.Text.Json;
using Seitenwerk.Core.Modell;

namespace Seitenwerk.Core.Dateiformat;

public class DateiFehler : Exception
{
    public DateiFehler(string meldung, Exception? innere = null) : base(meldung, innere) { }
}

/// <summary>Speichert und lädt Dokumente im eigenen Format (.swk, ein ZIP-Container mit dokument.json und medien/).</summary>
public static class DokumentSpeicher
{
    public const string Endung = ".swk";
    private const string JsonName = "dokument.json";

    public static void Speichern(Dokument dokument, string pfad)
    {
        // Erst in eine temporäre Datei schreiben, damit eine vorhandene Datei bei einem Fehler nicht zerstört wird.
        var temp = pfad + ".tmp";
        try
        {
            using (var datei = File.Create(temp))
                Speichern(dokument, datei);
            File.Move(temp, pfad, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    public static void Speichern(Dokument dokument, Stream ziel)
    {
        BereinigeMedien(dokument);
        using var zip = new ZipArchive(ziel, ZipArchiveMode.Create, leaveOpen: true);

        var json = zip.CreateEntry(JsonName, CompressionLevel.Optimal);
        using (var s = json.Open())
            JsonSerializer.Serialize(s, dokument, ModellJson.Optionen);

        foreach (var medium in dokument.Medien)
        {
            // Bilder sind meist schon komprimiert.
            var eintrag = zip.CreateEntry(MedienPfad(medium), CompressionLevel.NoCompression);
            using var s = eintrag.Open();
            s.Write(medium.Daten, 0, medium.Daten.Length);
        }
    }

    public static Dokument Laden(string pfad)
    {
        try
        {
            using var datei = File.OpenRead(pfad);
            return Laden(datei);
        }
        catch (IOException ex)
        {
            throw new DateiFehler($"Die Datei „{Path.GetFileName(pfad)}“ konnte nicht gelesen werden: {ex.Message}", ex);
        }
    }

    public static Dokument Laden(Stream quelle)
    {
        try
        {
            using var zip = new ZipArchive(quelle, ZipArchiveMode.Read, leaveOpen: true);
            var jsonEintrag = zip.GetEntry(JsonName) ?? throw new DateiFehler("Das ist keine Seitenwerk-Datei (dokument.json fehlt).");

            Dokument dokument;
            using (var s = jsonEintrag.Open())
                dokument = JsonSerializer.Deserialize<Dokument>(s, ModellJson.Optionen)
                    ?? throw new DateiFehler("Die Datei ist leer oder beschädigt.");

            if (dokument.Version > Dokument.AktuelleVersion)
                throw new DateiFehler("Die Datei stammt aus einer neueren Programmversion und kann nicht geöffnet werden.");

            foreach (var medium in dokument.Medien)
            {
                var eintrag = zip.GetEntry(MedienPfad(medium));
                if (eintrag == null)
                    continue;
                using var s = eintrag.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                medium.Daten = ms.ToArray();
            }

            if (dokument.Seiten.Count == 0)
                dokument.Seiten.Add(new Seite());
            return dokument;
        }
        catch (InvalidDataException ex)
        {
            throw new DateiFehler("Die Datei ist beschädigt oder keine Seitenwerk-Datei.", ex);
        }
        catch (JsonException ex)
        {
            throw new DateiFehler("Der Inhalt der Datei ist beschädigt.", ex);
        }
    }

    private static string MedienPfad(Medium m) => $"medien/{m.Id}.{m.Endung}";

    /// <summary>Entfernt Bilder, die von keinem Objekt mehr verwendet werden.</summary>
    public static void BereinigeMedien(Dokument dokument)
    {
        var benutzt = dokument.Seiten
            .SelectMany(s => s.Alle())
            .OfType<BildObjekt>()
            .Select(b => b.MedienId)
            .ToHashSet();
        dokument.Medien.RemoveAll(m => !benutzt.Contains(m.Id));
    }

    /// <summary>Serialisiert das Dokument ohne Bilddaten (für Rückgängig/Wiederholen und die Zwischenablage).</summary>
    public static string Momentaufnahme(Dokument dokument) => JsonSerializer.Serialize(dokument, ModellJson.Optionen);

    /// <summary>Stellt ein Dokument aus einer Momentaufnahme wieder her; die Bilddaten stammen aus <paramref name="medien"/>.</summary>
    public static Dokument AusMomentaufnahme(string json, IEnumerable<Medium> medien)
    {
        var dokument = JsonSerializer.Deserialize<Dokument>(json, ModellJson.Optionen)
            ?? throw new DateiFehler("Die Momentaufnahme ist ungültig.");
        var daten = medien.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First().Daten);
        foreach (var m in dokument.Medien)
            if (daten.TryGetValue(m.Id, out var bytes))
                m.Daten = bytes;
        return dokument;
    }
}
