using Seitenwerk.Core.Modell;

namespace Seitenwerk.Core.Bearbeiten;

public enum AusrichtenArt
{
    Links,
    Zentriert,
    Rechts,
    Oben,
    Mitte,
    Unten,
}

public enum Bezugsrahmen
{
    Seite,
    Auswahl,
}

/// <summary>Reihenfolge, Ausrichten, Verteilen, Gruppieren: reine Operationen auf dem Modell.</summary>
public static class Anordnung
{
    public static void InDenVordergrund(Seite seite, IReadOnlyCollection<Objekt> auswahl)
    {
        var ids = auswahl.Select(o => o.Id).ToHashSet();
        var bewegt = seite.Objekte.Where(o => ids.Contains(o.Id)).ToList();
        seite.Objekte.RemoveAll(o => ids.Contains(o.Id));
        seite.Objekte.AddRange(bewegt);
    }

    public static void InDenHintergrund(Seite seite, IReadOnlyCollection<Objekt> auswahl)
    {
        var ids = auswahl.Select(o => o.Id).ToHashSet();
        var bewegt = seite.Objekte.Where(o => ids.Contains(o.Id)).ToList();
        seite.Objekte.RemoveAll(o => ids.Contains(o.Id));
        seite.Objekte.InsertRange(0, bewegt);
    }

    public static void EineEbeneNachVorn(Seite seite, IReadOnlyCollection<Objekt> auswahl)
    {
        var ids = auswahl.Select(o => o.Id).ToHashSet();
        for (var i = seite.Objekte.Count - 2; i >= 0; i--)
        {
            if (ids.Contains(seite.Objekte[i].Id) && !ids.Contains(seite.Objekte[i + 1].Id))
                (seite.Objekte[i], seite.Objekte[i + 1]) = (seite.Objekte[i + 1], seite.Objekte[i]);
        }
    }

    public static void EineEbeneNachHinten(Seite seite, IReadOnlyCollection<Objekt> auswahl)
    {
        var ids = auswahl.Select(o => o.Id).ToHashSet();
        for (var i = 1; i < seite.Objekte.Count; i++)
        {
            if (ids.Contains(seite.Objekte[i].Id) && !ids.Contains(seite.Objekte[i - 1].Id))
                (seite.Objekte[i], seite.Objekte[i - 1]) = (seite.Objekte[i - 1], seite.Objekte[i]);
        }
    }

    public static void Ausrichten(IReadOnlyList<Objekt> objekte, AusrichtenArt art, (double X0, double Y0, double X1, double Y1) bezug)
    {
        foreach (var o in objekte)
        {
            var (x0, y0, x1, y1) = Geometrie.UmgebendesRechteck(o);
            double dx = 0, dy = 0;
            switch (art)
            {
                case AusrichtenArt.Links: dx = bezug.X0 - x0; break;
                case AusrichtenArt.Zentriert: dx = (bezug.X0 + bezug.X1) / 2 - (x0 + x1) / 2; break;
                case AusrichtenArt.Rechts: dx = bezug.X1 - x1; break;
                case AusrichtenArt.Oben: dy = bezug.Y0 - y0; break;
                case AusrichtenArt.Mitte: dy = (bezug.Y0 + bezug.Y1) / 2 - (y0 + y1) / 2; break;
                case AusrichtenArt.Unten: dy = bezug.Y1 - y1; break;
            }
            o.Verschieben(dx, dy);
        }
    }

    /// <summary>Verteilt mindestens drei Objekte mit gleichen Abständen zwischen den äußeren.</summary>
    public static void Verteilen(IReadOnlyList<Objekt> objekte, bool waagerecht)
    {
        if (objekte.Count < 3)
            return;
        var sortiert = objekte
            .Select(o => (Objekt: o, Rechteck: Geometrie.UmgebendesRechteck(o)))
            .OrderBy(t => waagerecht ? t.Rechteck.X0 : t.Rechteck.Y0)
            .ToList();

        var anfang = waagerecht ? sortiert[0].Rechteck.X0 : sortiert[0].Rechteck.Y0;
        var ende = waagerecht ? sortiert[^1].Rechteck.X1 : sortiert[^1].Rechteck.Y1;
        var gesamt = sortiert.Sum(t => waagerecht ? t.Rechteck.X1 - t.Rechteck.X0 : t.Rechteck.Y1 - t.Rechteck.Y0);
        var luecke = (ende - anfang - gesamt) / (sortiert.Count - 1);

        var position = anfang;
        foreach (var (objekt, r) in sortiert)
        {
            var breite = waagerecht ? r.X1 - r.X0 : r.Y1 - r.Y0;
            var delta = position - (waagerecht ? r.X0 : r.Y0);
            if (waagerecht)
                objekt.Verschieben(delta, 0);
            else
                objekt.Verschieben(0, delta);
            position += breite + luecke;
        }
    }

    /// <summary>Gruppiert die Objekte; die Gruppe steht an der Stelle des obersten Objekts. Gibt null zurück, wenn weniger als zwei Objekte gewählt sind.</summary>
    public static GruppeObjekt? Gruppieren(Seite seite, IReadOnlyCollection<Objekt> auswahl)
    {
        var ids = auswahl.Select(o => o.Id).ToHashSet();
        var mitglieder = seite.Objekte.Where(o => ids.Contains(o.Id)).ToList();
        if (mitglieder.Count < 2)
            return null;
        var einfuegePosition = seite.Objekte.FindLastIndex(o => ids.Contains(o.Id));
        var gruppe = GruppeObjekt.Aus(mitglieder);
        seite.Objekte.RemoveAll(o => ids.Contains(o.Id));
        var index = Math.Min(einfuegePosition - mitglieder.Count + 1, seite.Objekte.Count);
        seite.Objekte.Insert(Math.Max(0, index), gruppe);
        return gruppe;
    }

    public static IReadOnlyList<Objekt> Aufloesen(Seite seite, GruppeObjekt gruppe)
    {
        var index = seite.Objekte.FindIndex(o => o.Id == gruppe.Id);
        if (index < 0)
            return Array.Empty<Objekt>();
        seite.Objekte.RemoveAt(index);
        seite.Objekte.InsertRange(index, gruppe.Kinder);
        return gruppe.Kinder.ToList();
    }

    public static void Drehen(Objekt objekt, double grad)
    {
        if (objekt is GruppeObjekt gruppe)
        {
            // Gruppen werden in 90°-Schritten gedreht, indem jedes Kind um die Gruppenmitte gedreht wird.
            foreach (var kind in gruppe.Kinder)
            {
                var (nx, ny) = Geometrie.Drehen(kind.MitteX, kind.MitteY, gruppe.MitteX, gruppe.MitteY, grad);
                kind.X = nx - kind.Breite / 2;
                kind.Y = ny - kind.Hoehe / 2;
                kind.Drehung = Normieren(kind.Drehung + grad);
            }
            gruppe.GrenzenNeuBerechnen();
            return;
        }
        objekt.Drehung = Normieren(objekt.Drehung + grad);
    }

    public static double Normieren(double grad)
    {
        var w = grad % 360;
        if (w < 0)
            w += 360;
        return Math.Abs(w - 360) < 1e-9 ? 0 : Math.Round(w, 4);
    }
}
