using System.Text.Json.Serialization;

namespace Seitenwerk.Core.Modell;

public class Seite
{
    public string Id { get; set; } = Objekt.NeueId();
    public string Hintergrund { get; set; } = "#FFFFFF";
    public List<Objekt> Objekte { get; set; } = new();

    public Seite Kopie()
    {
        var kopie = ModellJson.Klonen(this);
        kopie.Id = Objekt.NeueId();
        foreach (var o in kopie.Objekte)
            o.NeueIdsVergeben();
        return kopie;
    }

    /// <summary>Sucht ein Objekt (auch in Gruppen) anhand der Id.</summary>
    public Objekt? Finde(string id) => Alle().FirstOrDefault(o => o.Id == id);

    /// <summary>Alle Objekte inklusive der Kinder von Gruppen.</summary>
    public IEnumerable<Objekt> Alle()
    {
        foreach (var o in Objekte)
        {
            yield return o;
            foreach (var k in Unterobjekte(o))
                yield return k;
        }

        static IEnumerable<Objekt> Unterobjekte(Objekt o)
        {
            foreach (var k in o.Unterobjekte)
            {
                yield return k;
                foreach (var kk in Unterobjekte(k))
                    yield return kk;
            }
        }
    }
}

public class Seitenraender
{
    public double Oben { get; set; } = 10;
    public double Unten { get; set; } = 10;
    public double Links { get; set; } = 10;
    public double Rechts { get; set; } = 10;
}

/// <summary>Ein eingebettetes Bild. Die Daten liegen nicht im JSON, sondern als eigene Datei im Dokumentcontainer.</summary>
public class Medium
{
    public string Id { get; set; } = Objekt.NeueId();

    /// <summary>Dateiendung ohne Punkt (png, jpg, ...).</summary>
    public string Endung { get; set; } = "png";

    [JsonIgnore]
    public byte[] Daten { get; set; } = Array.Empty<byte>();
}

public class Dokument
{
    public const int AktuelleVersion = 1;

    public int Version { get; set; } = AktuelleVersion;
    public string? Titel { get; set; }

    /// <summary>Seitenbreite in Millimetern (alle Seiten sind gleich groß).</summary>
    public double SeitenBreite { get; set; } = 210;
    public double SeitenHoehe { get; set; } = 297;
    public Seitenraender Raender { get; set; } = new();
    public List<Seite> Seiten { get; set; } = new() { new Seite() };
    public List<Medium> Medien { get; set; } = new();

    public Medium? FindeMedium(string id) => Medien.FirstOrDefault(m => m.Id == id);

    public Medium MediumHinzufuegen(byte[] daten, string endung)
    {
        var medium = new Medium { Daten = daten, Endung = endung.TrimStart('.').ToLowerInvariant() };
        Medien.Add(medium);
        return medium;
    }

    public Seite? SeiteVon(Objekt objekt) => Seiten.FirstOrDefault(s => s.Alle().Any(o => o.Id == objekt.Id));

    public static Dokument Neu(double breiteMm = 210, double hoeheMm = 297) =>
        new() { SeitenBreite = breiteMm, SeitenHoehe = hoeheMm };
}

public record Seitenformat(string Name, double Breite, double Hoehe)
{
    public static readonly Seitenformat[] Standard =
    {
        new("A4 (210 × 297 mm)", 210, 297),
        new("A5 (148 × 210 mm)", 148, 210),
        new("A3 (297 × 420 mm)", 297, 420),
        new("A6 / Postkarte (105 × 148 mm)", 105, 148),
        new("DIN lang / Flyer (99 × 210 mm)", 99, 210),
        new("Visitenkarte (85 × 55 mm)", 85, 55),
        new("Letter (216 × 279 mm)", 215.9, 279.4),
        new("Legal (216 × 356 mm)", 215.9, 355.6),
        new("Poster A2 (420 × 594 mm)", 420, 594),
        new("Poster A1 (594 × 841 mm)", 594, 841),
        new("Quadrat (200 × 200 mm)", 200, 200),
    };
}
