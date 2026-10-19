using System.IO;
using System.Text.Json;
using System.Windows;
using Seitenwerk.Core.Dateiformat;
using Seitenwerk.Core.Modell;

namespace Seitenwerk.Steuerung;

public sealed partial class EditorSitzung
{
    private const string Format = "Seitenwerk.Objekte";

    private sealed class KlemmbrettInhalt
    {
        public List<Objekt> Objekte { get; set; } = new();
        public List<MediumKopie> Medien { get; set; } = new();
    }

    private sealed class MediumKopie
    {
        public string Id { get; set; } = "";
        public string Endung { get; set; } = "png";
        public byte[] Daten { get; set; } = Array.Empty<byte>();
    }

    public void Kopieren()
    {
        if (Textbearbeitung != null)
            return; // Im Textfeld übernimmt die RichTextBox das Kopieren selbst.
        if (_auswahl.Count == 0)
            return;

        var inhalt = new KlemmbrettInhalt { Objekte = _auswahl.Select(o => o.Kopie()).ToList() };
        foreach (var id in inhalt.Objekte.SelectMany(o => Blaetter(o)).OfType<BildObjekt>().Select(b => b.MedienId).Distinct())
        {
            var medium = Dokument.FindeMedium(id);
            if (medium != null)
                inhalt.Medien.Add(new MediumKopie { Id = medium.Id, Endung = medium.Endung, Daten = medium.Daten });
        }

        var daten = new DataObject();
        daten.SetData(Format, JsonSerializer.Serialize(inhalt, ModellJson.Optionen));
        var text = string.Join(Environment.NewLine, _auswahl.OfType<TextTraeger>().Where(t => t.HatText).Select(t => t.Text));
        if (text.Length > 0)
            daten.SetText(text);
        try
        {
            Clipboard.SetDataObject(daten, true);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Die Zwischenablage ist gerade von einem anderen Programm gesperrt.
        }
    }

    public void Ausschneiden()
    {
        if (Textbearbeitung != null)
            return;
        Kopieren();
        AuswahlLoeschen();
    }

    public bool KannEinfuegen()
    {
        try
        {
            return Clipboard.ContainsData(Format) || Clipboard.ContainsImage() || Clipboard.ContainsText();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    /// <summary>Fügt Objekte, ein Bild oder Text aus der Zwischenablage auf der aktuellen Seite ein.</summary>
    public void Einfuegen()
    {
        if (Textbearbeitung != null)
            return;
        var seite = AktuelleSeiteModell;
        if (seite == null)
            return;

        try
        {
            if (Clipboard.GetData(Format) is string json)
            {
                var inhalt = JsonSerializer.Deserialize<KlemmbrettInhalt>(json, ModellJson.Optionen);
                if (inhalt != null && inhalt.Objekte.Count > 0)
                {
                    foreach (var m in inhalt.Medien)
                    {
                        if (Dokument.FindeMedium(m.Id) == null)
                            Dokument.Medien.Add(new Medium { Id = m.Id, Endung = m.Endung, Daten = m.Daten });
                    }
                    var versatz = ZaehleEinfuegen++ % 10 * 3 + 5;
                    _auswahl.Clear();
                    foreach (var o in inhalt.Objekte)
                    {
                        o.NeueIdsVergeben();
                        o.Verschieben(versatz, versatz);
                        seite.Objekte.Add(o);
                        _auswahl.Add(o);
                    }
                    AuswahlGeaendert?.Invoke();
                    Abschliessen();
                    return;
                }
            }

            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } bildQuelle)
            {
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bildQuelle));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                BildEinfuegen(ms.ToArray(), seite);
                return;
            }

            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text))
                    return;
                var feld = TextfeldObjekt.Neu(
                    Dokument.Raender.Links + 5,
                    Dokument.Raender.Oben + 5,
                    Math.Min(120, Dokument.SeitenBreite - Dokument.Raender.Links - Dokument.Raender.Rechts - 10),
                    40);
                feld.TextSetzen(text);
                ObjektHinzufuegen(seite, feld);
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Zwischenablage gesperrt
        }
        catch (JsonException)
        {
            // Beschädigte Daten in der Zwischenablage werden ignoriert.
        }
    }

    private int ZaehleEinfuegen { get; set; }
}
