namespace Seitenwerk.Core.Modell;

public enum Ausrichtung
{
    Links,
    Zentriert,
    Rechts,
    Blocksatz,
}

public enum VertikaleAusrichtung
{
    Oben,
    Mitte,
    Unten,
}

/// <summary>Zusammenhängender Text mit einheitlicher Zeichenformatierung. Ein "\n" im Text ist ein Zeilenumbruch innerhalb des Absatzes.</summary>
public class Textlauf
{
    public const string StandardSchriftart = "Calibri";
    public const double StandardGroesse = 12;

    public string Text { get; set; } = "";
    public string Schriftart { get; set; } = StandardSchriftart;

    /// <summary>Schriftgröße in Punkt.</summary>
    public double Groesse { get; set; } = StandardGroesse;
    public bool Fett { get; set; }
    public bool Kursiv { get; set; }
    public bool Unterstrichen { get; set; }
    public bool Durchgestrichen { get; set; }
    public string Farbe { get; set; } = "#000000";

    public Textlauf() { }

    public Textlauf(string text) => Text = text;

    public Textlauf Kopie()
    {
        return (Textlauf)MemberwiseClone();
    }

    public bool GleichesFormat(Textlauf anderer) =>
        Schriftart == anderer.Schriftart
        && Groesse == anderer.Groesse
        && Fett == anderer.Fett
        && Kursiv == anderer.Kursiv
        && Unterstrichen == anderer.Unterstrichen
        && Durchgestrichen == anderer.Durchgestrichen
        && Farbe == anderer.Farbe;
}

public class Absatz
{
    public List<Textlauf> Laeufe { get; set; } = new();
    public Ausrichtung Ausrichtung { get; set; } = Ausrichtung.Links;

    /// <summary>Faktor auf den Standard-Zeilenabstand der Schrift (1,0 = einfach).</summary>
    public double Zeilenabstand { get; set; } = 1.0;

    /// <summary>Abstand nach dem Absatz in Punkt.</summary>
    public double AbstandNach { get; set; }

    public Absatz() { }

    public Absatz(string text, Ausrichtung ausrichtung = Ausrichtung.Links)
    {
        Laeufe.Add(new Textlauf(text));
        Ausrichtung = ausrichtung;
    }

    public string Text => string.Concat(Laeufe.Select(l => l.Text));

    public Absatz Kopie()
    {
        var a = (Absatz)MemberwiseClone();
        a.Laeufe = Laeufe.Select(l => l.Kopie()).ToList();
        return a;
    }

    /// <summary>Fasst benachbarte Läufe mit gleichem Format zusammen und entfernt leere Läufe (mindestens ein Lauf bleibt).</summary>
    public void Bereinigen()
    {
        var neu = new List<Textlauf>();
        foreach (var lauf in Laeufe)
        {
            if (lauf.Text.Length == 0)
                continue;
            if (neu.Count > 0 && neu[^1].GleichesFormat(lauf))
                neu[^1].Text += lauf.Text;
            else
                neu.Add(lauf);
        }
        if (neu.Count == 0)
            neu.Add(Laeufe.Count > 0 ? new Textlauf { Schriftart = Laeufe[0].Schriftart, Groesse = Laeufe[0].Groesse, Farbe = Laeufe[0].Farbe, Fett = Laeufe[0].Fett, Kursiv = Laeufe[0].Kursiv } : new Textlauf());
        Laeufe = neu;
    }
}
