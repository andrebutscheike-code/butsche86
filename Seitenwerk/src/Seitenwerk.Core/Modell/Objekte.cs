using System.Text.Json;
using System.Text.Json.Serialization;

namespace Seitenwerk.Core.Modell;

public enum FormArt
{
    Rechteck,
    AbgerundetesRechteck,
    Ellipse,
    Dreieck,
    RechtwinkligesDreieck,
    Raute,
    Fuenfeck,
    Sechseck,
    Pfeil,
    Stern,
}

public enum Strichart
{
    Durchgezogen,
    Gestrichelt,
    Gepunktet,
}

public enum Pfeilspitze
{
    Keine,
    Pfeil,
    Kreis,
}

/// <summary>Grundlage aller Seitenobjekte. Alle Maße in Millimetern, Drehung in Grad im Uhrzeigersinn um die Mitte.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "art")]
[JsonDerivedType(typeof(TextfeldObjekt), "textfeld")]
[JsonDerivedType(typeof(FormObjekt), "form")]
[JsonDerivedType(typeof(LinieObjekt), "linie")]
[JsonDerivedType(typeof(BildObjekt), "bild")]
[JsonDerivedType(typeof(PfadObjekt), "pfad")]
[JsonDerivedType(typeof(GruppeObjekt), "gruppe")]
public abstract class Objekt
{
    public string Id { get; set; } = NeueId();
    public double X { get; set; }
    public double Y { get; set; }
    public double Breite { get; set; }
    public double Hoehe { get; set; }
    public double Drehung { get; set; }
    public bool Gesperrt { get; set; }
    public string? Name { get; set; }

    public static string NeueId() => Guid.NewGuid().ToString("N");

    [JsonIgnore]
    public double MitteX => X + Breite / 2;

    [JsonIgnore]
    public double MitteY => Y + Hoehe / 2;

    /// <summary>Tiefe Kopie mit neuen Ids.</summary>
    public Objekt Kopie()
    {
        var kopie = ModellJson.Klonen(this);
        kopie.NeueIdsVergeben();
        return kopie;
    }

    public virtual void NeueIdsVergeben() => Id = NeueId();

    public virtual void Verschieben(double dx, double dy)
    {
        X += dx;
        Y += dy;
    }

    /// <summary>Skaliert Position und Größe relativ zu einem Fixpunkt. Gruppen skalieren ihre Kinder mit.</summary>
    public virtual void Skalieren(double faktorX, double faktorY, double fixX, double fixY)
    {
        X = fixX + (X - fixX) * faktorX;
        Y = fixY + (Y - fixY) * faktorY;
        Breite *= faktorX;
        Hoehe *= faktorY;
    }

    /// <summary>Kinder (nur Gruppen), sonst leer.</summary>
    [JsonIgnore]
    public virtual IEnumerable<Objekt> Unterobjekte => Enumerable.Empty<Objekt>();
}

/// <summary>Objekte mit Füllung und Rahmen.</summary>
public abstract class FlaechenObjekt : Objekt
{
    /// <summary>Füllfarbe als "#RRGGBB" oder "#AARRGGBB"; null = keine Füllung.</summary>
    public string? Fuellung { get; set; }

    /// <summary>Rahmenfarbe; null = kein Rahmen.</summary>
    public string? Rahmenfarbe { get; set; }

    /// <summary>Rahmenstärke in Punkt.</summary>
    public double Rahmenbreite { get; set; } = 1;
    public Strichart Rahmenart { get; set; } = Strichart.Durchgezogen;

    /// <summary>Deckkraft 0..1.</summary>
    public double Deckkraft { get; set; } = 1;
}

/// <summary>Objekte, die Text enthalten können (Textfeld, Form).</summary>
public abstract class TextTraeger : FlaechenObjekt
{
    public List<Absatz> Absaetze { get; set; } = new() { new Absatz("") };

    /// <summary>Abstand zwischen Rand und Text in Millimetern.</summary>
    public double Innenabstand { get; set; } = 2.5;
    public VertikaleAusrichtung VertikaleAusrichtung { get; set; } = VertikaleAusrichtung.Oben;

    [JsonIgnore]
    public string Text => string.Join("\n", Absaetze.Select(a => a.Text));

    [JsonIgnore]
    public bool HatText => Absaetze.Any(a => a.Text.Length > 0);

    public void TextSetzen(string text, Textlauf? format = null)
    {
        Absaetze = text.Replace("\r\n", "\n").Split('\n').Select(zeile =>
        {
            var lauf = format?.Kopie() ?? new Textlauf();
            lauf.Text = zeile;
            var absatz = new Absatz();
            absatz.Laeufe.Add(lauf);
            return absatz;
        }).ToList();
    }
}

public class TextfeldObjekt : TextTraeger
{
    public static TextfeldObjekt Neu(double x, double y, double breite, double hoehe) =>
        new() { X = x, Y = y, Breite = breite, Hoehe = hoehe };
}

public class FormObjekt : TextTraeger
{
    public FormArt Form { get; set; } = FormArt.Rechteck;

    /// <summary>Eckenradius in Millimetern (nur bei AbgerundetesRechteck).</summary>
    public double Eckenradius { get; set; } = 5;

    public FormObjekt()
    {
        Fuellung = "#4472C4";
        Rahmenfarbe = "#2F528F";
        VertikaleAusrichtung = VertikaleAusrichtung.Mitte;
        Absaetze = new List<Absatz> { new("", Ausrichtung.Zentriert) };
    }
}

public class LinieObjekt : Objekt
{
    public string Farbe { get; set; } = "#000000";

    /// <summary>Linienstärke in Punkt.</summary>
    public double Staerke { get; set; } = 1.5;
    public Strichart Art { get; set; } = Strichart.Durchgezogen;
    public Pfeilspitze Anfang { get; set; } = Pfeilspitze.Keine;
    public Pfeilspitze Ende { get; set; } = Pfeilspitze.Keine;

    /// <summary>false: von links oben nach rechts unten; true: von links unten nach rechts oben.</summary>
    public bool Aufsteigend { get; set; }

    /// <summary>true: Der Anfang der Linie (für Pfeilspitzen) liegt am linken Ende, sonst am rechten.</summary>
    public bool AnfangLinks { get; set; } = true;

    /// <summary>Anfangs- und Endpunkt der Linie in Seitenkoordinaten (ohne Drehung).</summary>
    [JsonIgnore]
    public (double X1, double Y1, double X2, double Y2) Punkte
    {
        get
        {
            var links = Aufsteigend ? (X: X, Y: Y + Hoehe) : (X: X, Y: Y);
            var rechts = Aufsteigend ? (X: X + Breite, Y: Y) : (X: X + Breite, Y: Y + Hoehe);
            return AnfangLinks ? (links.X, links.Y, rechts.X, rechts.Y) : (rechts.X, rechts.Y, links.X, links.Y);
        }
    }

    public static LinieObjekt Zwischen(double x1, double y1, double x2, double y2)
    {
        var anfangLinks = x1 <= x2;
        var (ly, ry) = anfangLinks ? (y1, y2) : (y2, y1);
        return new LinieObjekt
        {
            X = Math.Min(x1, x2),
            Y = Math.Min(y1, y2),
            Breite = Math.Abs(x2 - x1),
            Hoehe = Math.Abs(y2 - y1),
            Aufsteigend = ry < ly,
            AnfangLinks = anfangLinks,
        };
    }
}

public class BildObjekt : FlaechenObjekt
{
    public string MedienId { get; set; } = "";

    /// <summary>Seitenverhältnis (Breite/Höhe) des Originalbilds.</summary>
    public double Seitenverhaeltnis { get; set; } = 1;
    public string? Beschreibung { get; set; }

    /// <summary>Beschnitt als Anteil 0..1 von der jeweiligen Kante.</summary>
    public double BeschnittLinks { get; set; }
    public double BeschnittRechts { get; set; }
    public double BeschnittOben { get; set; }
    public double BeschnittUnten { get; set; }
}

/// <summary>Freiform aus SVG-Pfaddaten (entsteht vor allem beim Import fremder Dateien).</summary>
public class PfadObjekt : FlaechenObjekt
{
    /// <summary>Pfaddaten in der Syntax von SVG ("M 0 0 L 10 0 ... Z") in den Koordinaten der ViewBox.</summary>
    public string Daten { get; set; } = "";
    public double ViewX { get; set; }
    public double ViewY { get; set; }
    public double ViewBreite { get; set; } = 1;
    public double ViewHoehe { get; set; } = 1;
}

public class GruppeObjekt : Objekt
{
    public List<Objekt> Kinder { get; set; } = new();

    public override IEnumerable<Objekt> Unterobjekte => Kinder;

    public static GruppeObjekt Aus(IEnumerable<Objekt> objekte)
    {
        var g = new GruppeObjekt { Kinder = objekte.ToList() };
        g.GrenzenNeuBerechnen();
        return g;
    }

    public void GrenzenNeuBerechnen()
    {
        if (Kinder.Count == 0)
            return;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var k in Kinder)
        {
            var (x0, y0, x1, y1) = Geometrie.UmgebendesRechteck(k);
            minX = Math.Min(minX, x0);
            minY = Math.Min(minY, y0);
            maxX = Math.Max(maxX, x1);
            maxY = Math.Max(maxY, y1);
        }
        X = minX;
        Y = minY;
        Breite = maxX - minX;
        Hoehe = maxY - minY;
    }

    public override void Verschieben(double dx, double dy)
    {
        base.Verschieben(dx, dy);
        foreach (var k in Kinder)
            k.Verschieben(dx, dy);
    }

    public override void Skalieren(double faktorX, double faktorY, double fixX, double fixY)
    {
        base.Skalieren(faktorX, faktorY, fixX, fixY);
        foreach (var k in Kinder)
            k.Skalieren(faktorX, faktorY, fixX, fixY);
    }

    public override void NeueIdsVergeben()
    {
        base.NeueIdsVergeben();
        foreach (var k in Kinder)
            k.NeueIdsVergeben();
    }
}

internal static class ObjektErweiterungen
{
    public static T Mit<T>(this T objekt, Action<T> aktion)
    {
        aktion(objekt);
        return objekt;
    }
}

public static class ModellJson
{
    public static readonly JsonSerializerOptions Optionen = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T Klonen<T>(T objekt) where T : class
    {
        var json = JsonSerializer.Serialize(objekt, typeof(T), Optionen);
        return (T)JsonSerializer.Deserialize(json, typeof(T), Optionen)!;
    }
}
