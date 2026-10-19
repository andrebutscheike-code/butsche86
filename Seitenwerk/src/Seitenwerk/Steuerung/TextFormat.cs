using Seitenwerk.Core.Modell;

namespace Seitenwerk.Steuerung;

/// <summary>Eine gewünschte Änderung der Zeichen- und Absatzformatierung. Nicht gesetzte Werte (null) bleiben unverändert.</summary>
public sealed record TextFormat
{
    public string? Schriftart { get; init; }
    public double? Groesse { get; init; }

    /// <summary>Relative Änderung der Schriftgröße in Punkt (für „Schrift vergrößern/verkleinern“).</summary>
    public double? GroesseAendern { get; init; }
    public bool? Fett { get; init; }
    public bool? Kursiv { get; init; }
    public bool? Unterstrichen { get; init; }
    public bool? Durchgestrichen { get; init; }
    public string? Farbe { get; init; }
    public Ausrichtung? Ausrichtung { get; init; }
    public double? Zeilenabstand { get; init; }
    public VertikaleAusrichtung? Vertikal { get; init; }

    public void Anwenden(Textlauf lauf)
    {
        if (Schriftart != null) lauf.Schriftart = Schriftart;
        if (Groesse != null) lauf.Groesse = Math.Clamp(Groesse.Value, 1, 999);
        if (GroesseAendern != null) lauf.Groesse = Math.Clamp(lauf.Groesse + GroesseAendern.Value, 1, 999);
        if (Fett != null) lauf.Fett = Fett.Value;
        if (Kursiv != null) lauf.Kursiv = Kursiv.Value;
        if (Unterstrichen != null) lauf.Unterstrichen = Unterstrichen.Value;
        if (Durchgestrichen != null) lauf.Durchgestrichen = Durchgestrichen.Value;
        if (Farbe != null) lauf.Farbe = Farbe;
    }

    public void Anwenden(Absatz absatz)
    {
        if (Ausrichtung != null) absatz.Ausrichtung = Ausrichtung.Value;
        if (Zeilenabstand != null) absatz.Zeilenabstand = Zeilenabstand.Value;
    }
}

/// <summary>Formatierung an der aktuellen Auswahl bzw. Schreibmarke (für die Anzeige im Menüband). Null = gemischt.</summary>
public sealed record TextFormatZustand(
    string? Schriftart,
    double? Groesse,
    bool? Fett,
    bool? Kursiv,
    bool? Unterstrichen,
    bool? Durchgestrichen,
    string? Farbe,
    Ausrichtung? Ausrichtung);
