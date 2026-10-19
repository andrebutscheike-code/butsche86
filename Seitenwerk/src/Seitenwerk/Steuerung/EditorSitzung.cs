using System.Text.Json;
using Seitenwerk.Core.Bearbeiten;
using Seitenwerk.Core.Dateiformat;
using Seitenwerk.Core.Modell;

namespace Seitenwerk.Steuerung;

public enum Werkzeug
{
    Auswahl,
    Textfeld,
    Form,
    Linie,
    Pfeil,
}

/// <summary>
/// Zustand und Befehle eines geöffneten Dokuments: Auswahl, Rückgängig/Wiederholen, Seitenverwaltung, Zwischenablage, Formatierung.
/// Die Ansichten lesen nur und melden Änderungen über <see cref="Abschliessen"/> zurück.
/// </summary>
public sealed partial class EditorSitzung
{
    public const double MinZoom = 0.1;
    public const double MaxZoom = 8.0;

    private readonly UndoVerlauf _verlauf = new();
    private readonly Dictionary<string, Medium> _medienPool = new();
    private readonly List<Objekt> _auswahl = new();
    private string _gespeicherterStand = "";
    private string _aktuellerStand = "";
    private double _zoom = 1.0;
    private int _aktuelleSeite;
    private Werkzeug _werkzeug = Werkzeug.Auswahl;

    public EditorSitzung() : this(Dokument.Neu())
    {
    }

    public EditorSitzung(Dokument dokument) => NeuesDokument(dokument, null, false);

    // ---------------------------------------------------------------- Zustand

    public Dokument Dokument { get; private set; } = null!;
    public string? Pfad { get; set; }
    public bool Geaendert => _aktuellerStand != _gespeicherterStand;
    public IReadOnlyList<Objekt> Auswahl => _auswahl;
    public bool KannRueckgaengig => _verlauf.KannRueckgaengig;
    public bool KannWiederholen => _verlauf.KannWiederholen;

    /// <summary>Hinweise zum Import (z. B. aus .pub), die dem Benutzer angezeigt werden.</summary>
    public List<string> ImportHinweise { get; } = new();

    public ITextBearbeitung? Textbearbeitung { get; set; }

    public int AktuelleSeite
    {
        get => _aktuelleSeite;
        set
        {
            var neu = Math.Clamp(value, 0, Dokument.Seiten.Count - 1);
            if (neu == _aktuelleSeite)
                return;
            _aktuelleSeite = neu;
            ZustandGeaendert?.Invoke();
        }
    }

    public Seite? AktuelleSeiteModell => Dokument.Seiten.Count > 0 ? Dokument.Seiten[Math.Clamp(_aktuelleSeite, 0, Dokument.Seiten.Count - 1)] : null;

    /// <summary>Die Seite, auf der die ausgewählten Objekte liegen.</summary>
    public Seite? AuswahlSeite => _auswahl.Count == 0 ? null : Dokument.SeiteVon(_auswahl[0]);

    public double Zoom
    {
        get => _zoom;
        set
        {
            var neu = Math.Clamp(value, MinZoom, MaxZoom);
            if (Math.Abs(neu - _zoom) < 1e-6)
                return;
            _zoom = neu;
            ZoomGeaendert?.Invoke();
        }
    }

    public Werkzeug AktivesWerkzeug
    {
        get => _werkzeug;
        set
        {
            _werkzeug = value;
            WerkzeugGeaendert?.Invoke();
        }
    }

    public FormArt NeueForm { get; set; } = FormArt.Rechteck;

    // Ansichtsoptionen
    private bool _zeigeRaender = true;
    private bool _zeigeRaster;
    private bool _rasterEinrasten;
    private bool _objektFang = true;
    private bool _zeigeLineale = true;
    private bool _rechtschreibung = true;
    private bool _zeigeNavigation = true;

    public bool ZeigeRaender { get => _zeigeRaender; set => Option(ref _zeigeRaender, value); }
    public bool ZeigeRaster { get => _zeigeRaster; set => Option(ref _zeigeRaster, value); }
    public bool RasterEinrasten { get => _rasterEinrasten; set => Option(ref _rasterEinrasten, value); }
    public bool ObjektFang { get => _objektFang; set => Option(ref _objektFang, value); }
    public bool ZeigeLineale { get => _zeigeLineale; set => Option(ref _zeigeLineale, value); }
    public bool Rechtschreibung { get => _rechtschreibung; set => Option(ref _rechtschreibung, value); }
    public bool ZeigeNavigation { get => _zeigeNavigation; set => Option(ref _zeigeNavigation, value); }

    private void Option(ref bool feld, bool wert)
    {
        if (feld == wert)
            return;
        feld = wert;
        OptionenGeaendert?.Invoke();
    }

    // ---------------------------------------------------------------- Ereignisse

    /// <summary>Objekte wurden verändert: Seiten neu zeichnen.</summary>
    public event Action? InhaltGeaendert;

    /// <summary>Das Dokument wurde ersetzt oder Seiten kamen hinzu bzw. fielen weg: Ansichten neu aufbauen.</summary>
    public event Action? StrukturGeaendert;

    public event Action? AuswahlGeaendert;
    public event Action? ZoomGeaendert;
    public event Action? WerkzeugGeaendert;
    public event Action? OptionenGeaendert;

    /// <summary>Lage oder Größe der Auswahl hat sich während des Ziehens geändert (Statusleiste).</summary>
    public event Action? LageGeaendert;

    /// <summary>Die Markierung im bearbeiteten Textfeld hat sich geändert (Schriftanzeige im Menüband).</summary>
    public event Action? TextAuswahlGeaendert;

    public void MeldeLage() => LageGeaendert?.Invoke();
    public void MeldeTextAuswahl() => TextAuswahlGeaendert?.Invoke();

    /// <summary>Verwirft Änderungen seit dem letzten abgeschlossenen Schritt (Abbruch mit Esc während des Ziehens).</summary>
    public void LetztenStandWiederherstellen() => Wiederherstellen(_aktuellerStand);

    /// <summary>Titel, Änderungsmarkierung, Rückgängig-Verfügbarkeit, aktuelle Seite.</summary>
    public event Action? ZustandGeaendert;

    // ---------------------------------------------------------------- Dokument

    public void NeuesDokument(Dokument dokument, string? pfad, bool alsGeaendert)
    {
        BeendeTextbearbeitung();
        Dokument = dokument;
        Pfad = pfad;
        _medienPool.Clear();
        MerkeMedien();
        _auswahl.Clear();
        _aktuelleSeite = 0;
        _aktuellerStand = DokumentSpeicher.Momentaufnahme(dokument);
        _verlauf.Zuruecksetzen(_aktuellerStand);
        _gespeicherterStand = alsGeaendert ? "" : _aktuellerStand;
        StrukturGeaendert?.Invoke();
        AuswahlGeaendert?.Invoke();
        ZustandGeaendert?.Invoke();
    }

    private void MerkeMedien()
    {
        foreach (var m in Dokument.Medien)
            _medienPool[m.Id] = m;
    }

    /// <summary>Muss nach jeder abgeschlossenen Änderung aufgerufen werden (Loslassen der Maus, Befehl ausgeführt).</summary>
    public void Abschliessen()
    {
        MerkeMedien();
        _aktuellerStand = DokumentSpeicher.Momentaufnahme(Dokument);
        _verlauf.Merke(_aktuellerStand);
        InhaltGeaendert?.Invoke();
        ZustandGeaendert?.Invoke();
    }

    /// <summary>Zeichnet neu, ohne einen Rückgängig-Schritt anzulegen (während des Ziehens).</summary>
    public void Aktualisieren() => InhaltGeaendert?.Invoke();

    public void MarkiereAlsGespeichert()
    {
        _gespeicherterStand = _aktuellerStand;
        ZustandGeaendert?.Invoke();
    }

    public void BeendeTextbearbeitung() => Textbearbeitung?.Beenden(true);

    public void Rueckgaengig()
    {
        BeendeTextbearbeitung();
        var stand = _verlauf.Rueckgaengig();
        if (stand != null)
            Wiederherstellen(stand);
    }

    public void Wiederholen()
    {
        BeendeTextbearbeitung();
        var stand = _verlauf.Wiederholen();
        if (stand != null)
            Wiederherstellen(stand);
    }

    private void Wiederherstellen(string stand)
    {
        var ids = _auswahl.Select(o => o.Id).ToList();
        var seitenAnzahl = Dokument.Seiten.Count;

        Dokument = DokumentSpeicher.AusMomentaufnahme(stand, _medienPool.Values);
        _aktuellerStand = stand;
        _aktuelleSeite = Math.Clamp(_aktuelleSeite, 0, Dokument.Seiten.Count - 1);

        _auswahl.Clear();
        foreach (var id in ids)
        {
            var objekt = Dokument.Seiten.Select(s => s.Finde(id)).FirstOrDefault(o => o != null);
            if (objekt != null && Dokument.Seiten.Any(s => s.Objekte.Any(o => o.Id == objekt.Id)))
                _auswahl.Add(objekt);
        }

        if (seitenAnzahl != Dokument.Seiten.Count)
            StrukturGeaendert?.Invoke();
        else
            InhaltGeaendert?.Invoke();
        AuswahlGeaendert?.Invoke();
        ZustandGeaendert?.Invoke();
    }

    // ---------------------------------------------------------------- Auswahl

    public void AuswahlSetzen(IEnumerable<Objekt> objekte)
    {
        var neu = objekte.ToList();
        if (neu.SequenceEqual(_auswahl))
            return;
        if (Textbearbeitung != null && !(neu.Count == 1 && ReferenceEquals(neu[0], Textbearbeitung.Objekt)))
            BeendeTextbearbeitung();
        _auswahl.Clear();
        _auswahl.AddRange(neu);
        if (_auswahl.Count > 0 && Dokument.SeiteVon(_auswahl[0]) is { } seite)
            _aktuelleSeite = Dokument.Seiten.IndexOf(seite);
        AuswahlGeaendert?.Invoke();
        ZustandGeaendert?.Invoke();
    }

    public void AuswahlLeeren() => AuswahlSetzen(Array.Empty<Objekt>());

    public void AuswahlUmschalten(Objekt objekt)
    {
        var neu = _auswahl.ToList();
        if (!neu.Remove(objekt))
        {
            // Mehrfachauswahl nur innerhalb einer Seite.
            if (neu.Count > 0 && Dokument.SeiteVon(neu[0]) != Dokument.SeiteVon(objekt))
                neu.Clear();
            neu.Add(objekt);
        }
        AuswahlSetzen(neu);
    }

    public void AllesAuswaehlen()
    {
        var seite = AktuelleSeiteModell;
        if (seite != null)
            AuswahlSetzen(seite.Objekte.Where(o => !o.Gesperrt));
    }

    public bool IstAusgewaehlt(Objekt objekt) => _auswahl.Contains(objekt);

    public IReadOnlyList<TextTraeger> AusgewaehlteTexte => _auswahl.OfType<TextTraeger>().ToList();

    // ---------------------------------------------------------------- Objekte

    public void ObjektHinzufuegen(Seite seite, Objekt objekt, bool auswaehlen = true)
    {
        seite.Objekte.Add(objekt);
        if (auswaehlen)
        {
            _auswahl.Clear();
            _auswahl.Add(objekt);
            _aktuelleSeite = Dokument.Seiten.IndexOf(seite);
            AuswahlGeaendert?.Invoke();
        }
        Abschliessen();
    }

    public void AuswahlLoeschen()
    {
        BeendeTextbearbeitung();
        if (_auswahl.Count == 0)
            return;
        foreach (var objekt in _auswahl.Where(o => !o.Gesperrt).ToList())
            Entferne(objekt);
        _auswahl.Clear();
        AuswahlGeaendert?.Invoke();
        Abschliessen();
    }

    private void Entferne(Objekt objekt)
    {
        foreach (var seite in Dokument.Seiten)
            if (seite.Objekte.Remove(objekt))
                return;
    }

    public void AuswahlVerschieben(double dx, double dy)
    {
        foreach (var o in _auswahl.Where(o => !o.Gesperrt))
            o.Verschieben(dx, dy);
        Abschliessen();
    }

    public void AuswahlDuplizieren()
    {
        BeendeTextbearbeitung();
        var seite = AuswahlSeite;
        if (seite == null)
            return;
        var kopien = new List<Objekt>();
        foreach (var o in _auswahl)
        {
            var kopie = o.Kopie();
            kopie.Verschieben(5, 5);
            seite.Objekte.Add(kopie);
            kopien.Add(kopie);
        }
        _auswahl.Clear();
        _auswahl.AddRange(kopien);
        AuswahlGeaendert?.Invoke();
        Abschliessen();
    }

    // ---------------------------------------------------------------- Anordnen

    private bool MitAuswahl(Action<Seite, IReadOnlyList<Objekt>> aktion, int minimum = 1)
    {
        BeendeTextbearbeitung();
        var seite = AuswahlSeite;
        if (seite == null || _auswahl.Count < minimum)
            return false;
        aktion(seite, _auswahl.ToList());
        Abschliessen();
        return true;
    }

    public void InDenVordergrund() => MitAuswahl((s, a) => Anordnung.InDenVordergrund(s, a));
    public void InDenHintergrund() => MitAuswahl((s, a) => Anordnung.InDenHintergrund(s, a));
    public void EineEbeneNachVorn() => MitAuswahl((s, a) => Anordnung.EineEbeneNachVorn(s, a));
    public void EineEbeneNachHinten() => MitAuswahl((s, a) => Anordnung.EineEbeneNachHinten(s, a));

    public void Ausrichten(AusrichtenArt art, Bezugsrahmen bezug)
    {
        MitAuswahl((seite, objekte) =>
        {
            // Ein einzelnes Objekt wird immer an der Seite ausgerichtet.
            var rahmen = bezug == Bezugsrahmen.Auswahl && objekte.Count > 1
                ? Geometrie.UmgebendesRechteck(objekte)
                : (0.0, 0.0, Dokument.SeitenBreite, Dokument.SeitenHoehe);
            Anordnung.Ausrichten(objekte, art, rahmen);
        });
    }

    public void Verteilen(bool waagerecht) => MitAuswahl((_, o) => Anordnung.Verteilen(o, waagerecht), 3);

    public void Gruppieren()
    {
        BeendeTextbearbeitung();
        var seite = AuswahlSeite;
        if (seite == null)
            return;
        var gruppe = Anordnung.Gruppieren(seite, _auswahl.ToList());
        if (gruppe == null)
            return;
        _auswahl.Clear();
        _auswahl.Add(gruppe);
        AuswahlGeaendert?.Invoke();
        Abschliessen();
    }

    public void GruppierungAufheben()
    {
        BeendeTextbearbeitung();
        var seite = AuswahlSeite;
        if (seite == null)
            return;
        var neu = new List<Objekt>();
        var geaendert = false;
        foreach (var o in _auswahl)
        {
            if (o is GruppeObjekt g)
            {
                neu.AddRange(Anordnung.Aufloesen(seite, g));
                geaendert = true;
            }
            else
            {
                neu.Add(o);
            }
        }
        if (!geaendert)
            return;
        _auswahl.Clear();
        _auswahl.AddRange(neu);
        AuswahlGeaendert?.Invoke();
        Abschliessen();
    }

    public void Drehen(double grad)
    {
        MitAuswahl((_, objekte) =>
        {
            foreach (var o in objekte.Where(o => !o.Gesperrt))
                Anordnung.Drehen(o, grad);
        });
    }

    public void SperreUmschalten()
    {
        if (_auswahl.Count == 0)
            return;
        var sperren = _auswahl.Any(o => !o.Gesperrt);
        foreach (var o in _auswahl)
            o.Gesperrt = sperren;
        Abschliessen();
    }

    // ---------------------------------------------------------------- Format

    public void Fuellung(string? farbe)
    {
        foreach (var o in AlleFlaechen())
            o.Fuellung = farbe;
        Abschliessen();
    }

    public void Rahmenfarbe(string? farbe)
    {
        BeendeTextbearbeitung();
        foreach (var o in AlleFlaechen())
        {
            o.Rahmenfarbe = farbe;
            if (farbe != null && o.Rahmenbreite <= 0)
                o.Rahmenbreite = 1;
        }
        foreach (var l in AlleLinien())
            if (farbe != null)
                l.Farbe = farbe;
        Abschliessen();
    }

    public void Rahmenbreite(double punkt)
    {
        foreach (var o in AlleFlaechen())
        {
            o.Rahmenbreite = punkt;
            if (o.Rahmenfarbe == null && punkt > 0)
                o.Rahmenfarbe = "#000000";
        }
        foreach (var l in AlleLinien())
            l.Staerke = Math.Max(0.25, punkt);
        Abschliessen();
    }

    public void Rahmenart(Strichart art)
    {
        foreach (var o in AlleFlaechen())
            o.Rahmenart = art;
        foreach (var l in AlleLinien())
            l.Art = art;
        Abschliessen();
    }

    public void Pfeilspitzen(Pfeilspitze anfang, Pfeilspitze ende)
    {
        foreach (var l in AlleLinien())
        {
            l.Anfang = anfang;
            l.Ende = ende;
        }
        Abschliessen();
    }

    public void Deckkraft(double wert)
    {
        foreach (var o in AlleFlaechen())
            o.Deckkraft = Math.Clamp(wert, 0, 1);
        Abschliessen();
    }

    public void SeitenHintergrund(string farbe)
    {
        var seite = AktuelleSeiteModell;
        if (seite == null)
            return;
        seite.Hintergrund = farbe;
        Abschliessen();
    }

    public void FormAendern(FormArt form)
    {
        foreach (var f in _auswahl.OfType<FormObjekt>())
            f.Form = form;
        Abschliessen();
    }

    private IEnumerable<FlaechenObjekt> AlleFlaechen() => _auswahl.SelectMany(Blaetter).OfType<FlaechenObjekt>();
    private IEnumerable<LinieObjekt> AlleLinien() => _auswahl.SelectMany(Blaetter).OfType<LinieObjekt>();

    private static IEnumerable<Objekt> Blaetter(Objekt o) =>
        o is GruppeObjekt g ? g.Kinder.SelectMany(Blaetter) : new[] { o };

    // ---------------------------------------------------------------- Text

    /// <summary>Wendet Textformatierung auf die aktuelle Markierung im Textfeld an, sonst auf alle ausgewählten Textobjekte.</summary>
    public void TextFormatAnwenden(TextFormat format)
    {
        if (Textbearbeitung != null)
        {
            Textbearbeitung.Anwenden(format);
            return;
        }

        var ziele = _auswahl.SelectMany(Blaetter).OfType<TextTraeger>().ToList();
        if (ziele.Count == 0)
            return;
        foreach (var traeger in ziele)
        {
            foreach (var absatz in traeger.Absaetze)
            {
                format.Anwenden(absatz);
                foreach (var lauf in absatz.Laeufe)
                    format.Anwenden(lauf);
            }
            if (format.Vertikal != null)
                traeger.VertikaleAusrichtung = format.Vertikal.Value;
        }
        Abschliessen();
    }

    public TextFormatZustand? LeseTextFormat()
    {
        if (Textbearbeitung != null)
            return Textbearbeitung.LeseZustand();

        var laeufe = _auswahl.SelectMany(Blaetter).OfType<TextTraeger>().SelectMany(t => t.Absaetze).ToList();
        if (laeufe.Count == 0)
            return null;
        var alle = laeufe.SelectMany(a => a.Laeufe).ToList();
        if (alle.Count == 0)
            return null;

        T? Gleich<T>(Func<Textlauf, T> auswahl) where T : struct =>
            alle.Select(auswahl).Distinct().Count() == 1 ? auswahl(alle[0]) : null;

        string? GleichText(Func<Textlauf, string> auswahl) =>
            alle.Select(auswahl).Distinct().Count() == 1 ? auswahl(alle[0]) : null;

        return new TextFormatZustand(
            GleichText(l => l.Schriftart),
            Gleich(l => l.Groesse),
            Gleich(l => l.Fett),
            Gleich(l => l.Kursiv),
            Gleich(l => l.Unterstrichen),
            Gleich(l => l.Durchgestrichen),
            GleichText(l => l.Farbe),
            laeufe.Select(a => a.Ausrichtung).Distinct().Count() == 1 ? laeufe[0].Ausrichtung : null);
    }

    // ---------------------------------------------------------------- Seiten

    public Seite SeiteEinfuegen(int index)
    {
        BeendeTextbearbeitung();
        var seite = new Seite();
        index = Math.Clamp(index, 0, Dokument.Seiten.Count);
        Dokument.Seiten.Insert(index, seite);
        _aktuelleSeite = index;
        StrukturGeaendert?.Invoke();
        Abschliessen();
        return seite;
    }

    public void SeiteDuplizieren(int index)
    {
        BeendeTextbearbeitung();
        if (index < 0 || index >= Dokument.Seiten.Count)
            return;
        Dokument.Seiten.Insert(index + 1, Dokument.Seiten[index].Kopie());
        _aktuelleSeite = index + 1;
        StrukturGeaendert?.Invoke();
        Abschliessen();
    }

    public bool SeiteLoeschen(int index)
    {
        BeendeTextbearbeitung();
        if (Dokument.Seiten.Count <= 1 || index < 0 || index >= Dokument.Seiten.Count)
            return false;
        Dokument.Seiten.RemoveAt(index);
        _auswahl.Clear();
        _aktuelleSeite = Math.Clamp(_aktuelleSeite, 0, Dokument.Seiten.Count - 1);
        StrukturGeaendert?.Invoke();
        AuswahlGeaendert?.Invoke();
        Abschliessen();
        return true;
    }

    public void SeiteVerschieben(int von, int nach)
    {
        BeendeTextbearbeitung();
        if (von < 0 || von >= Dokument.Seiten.Count || nach < 0 || nach >= Dokument.Seiten.Count || von == nach)
            return;
        var seite = Dokument.Seiten[von];
        Dokument.Seiten.RemoveAt(von);
        Dokument.Seiten.Insert(nach, seite);
        _aktuelleSeite = nach;
        StrukturGeaendert?.Invoke();
        Abschliessen();
    }

    public void SeiteEinrichten(double breite, double hoehe, Seitenraender raender)
    {
        BeendeTextbearbeitung();
        Dokument.SeitenBreite = Math.Clamp(breite, 10, 5000);
        Dokument.SeitenHoehe = Math.Clamp(hoehe, 10, 5000);
        Dokument.Raender = raender;
        StrukturGeaendert?.Invoke();
        Abschliessen();
    }

    // ---------------------------------------------------------------- Bilder

    /// <summary>Fügt ein Bild aus Rohdaten ein. Die Größe wird auf die Seite begrenzt, die Mitte der Seite ist der Standardort.</summary>
    public BildObjekt? BildEinfuegen(byte[] daten, Seite seite, double? mitteX = null, double? mitteY = null)
    {
        var format = BildFormat.Erkennen(daten);
        if (format == null)
            return null;

        double breite, hoehe;
        var info = BildFormat.Groesse(daten);
        if (info != null)
        {
            breite = info.Value.BreitePixel / info.Value.DpiX * 25.4;
            hoehe = info.Value.HoehePixel / info.Value.DpiY * 25.4;
        }
        else
        {
            breite = 80;
            hoehe = 60;
        }

        // Nicht größer als die Seite innerhalb der Ränder.
        var maxB = Math.Max(10, Dokument.SeitenBreite - Dokument.Raender.Links - Dokument.Raender.Rechts);
        var maxH = Math.Max(10, Dokument.SeitenHoehe - Dokument.Raender.Oben - Dokument.Raender.Unten);
        var faktor = Math.Min(1.0, Math.Min(maxB / breite, maxH / hoehe));
        breite *= faktor;
        hoehe *= faktor;

        var medium = Dokument.MediumHinzufuegen(daten, format);
        var bild = new BildObjekt
        {
            MedienId = medium.Id,
            Breite = Math.Round(breite, 2),
            Hoehe = Math.Round(hoehe, 2),
            Seitenverhaeltnis = breite / hoehe,
        };
        bild.X = Math.Round((mitteX ?? Dokument.SeitenBreite / 2) - bild.Breite / 2, 2);
        bild.Y = Math.Round((mitteY ?? Dokument.SeitenHoehe / 2) - bild.Hoehe / 2, 2);
        ObjektHinzufuegen(seite, bild);
        return bild;
    }
}
