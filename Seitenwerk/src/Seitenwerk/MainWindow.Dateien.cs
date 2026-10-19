using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Seitenwerk.Core.Dateiformat;
using Seitenwerk.Core.Import;
using Seitenwerk.Core.Modell;
using Seitenwerk.Dialoge;
using Seitenwerk.Werkzeuge;

namespace Seitenwerk;

public partial class MainWindow
{
    private const string LibreOfficeAdresse = "https://de.libreoffice.org/download/download-libreoffice/";

    private string Dokumentname => Editor.Pfad != null ? Path.GetFileName(Editor.Pfad) : Editor.Dokument.Titel ?? "Unbenannt";

    /// <summary>Fragt bei ungespeicherten Änderungen nach. Gibt false zurück, wenn der Vorgang abgebrochen werden soll.</summary>
    private bool FrageSpeichern()
    {
        if (!Editor.Geaendert)
            return true;
        Editor.BeendeTextbearbeitung();
        var antwort = MessageBox.Show(this, $"Sollen die Änderungen an „{Dokumentname}“ gespeichert werden?", "Seitenwerk",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return antwort switch
        {
            MessageBoxResult.Cancel => false,
            MessageBoxResult.Yes => Speichern(),
            _ => true,
        };
    }

    private async Task SicherAsync(Func<Task> aktion)
    {
        try
        {
            await aktion();
        }
        catch (OperationCanceledException)
        {
            // vom Benutzer abgebrochen
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DateiFehler or ImportFehler or NotSupportedException)
        {
            MessageBox.Show(this, ex.Message, "Seitenwerk", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------------------------------------------------------------- Neu und Öffnen

    private Task DateiNeuAsync() => SicherAsync(() =>
    {
        if (!FrageSpeichern())
            return Task.CompletedTask;
        var dialog = new SeiteEinrichtenDialog("Neues Dokument", 210, 297, new Seitenraender()) { Owner = this };
        if (dialog.ShowDialog() != true)
            return Task.CompletedTask;
        var dokument = Dokument.Neu(dialog.Breite, dialog.Hoehe);
        dokument.Raender = dialog.Raender;
        Editor.NeuesDokument(dokument, null, false);
        NachDokumentwechsel();
        return Task.CompletedTask;
    });

    private void NachDokumentwechsel() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            ZoomAnpassen(ZoomModus.GanzeSeite);
            Bildlauf.ScrollToTop();
        });

    private Task DateiOeffnenDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Öffnen",
            Filter = "Alle unterstützten Dateien|*.swk;*.pub;*.odg|Seitenwerk-Dokumente (*.swk)|*.swk|Microsoft Publisher (*.pub)|*.pub|OpenDocument-Zeichnung (*.odg)|*.odg|Alle Dateien|*.*",
        };
        return dialog.ShowDialog(this) == true ? OeffneDateiAsync(dialog.FileName) : Task.CompletedTask;
    }

    public Task OeffneDateiAsync(string pfad, bool ohneNachfrage = false) => SicherAsync(async () =>
    {
        if (!ohneNachfrage && !FrageSpeichern())
            return;
        if (!File.Exists(pfad))
        {
            MessageBox.Show(this, $"Die Datei „{pfad}“ wurde nicht gefunden.", "Seitenwerk", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        switch (Path.GetExtension(pfad).ToLowerInvariant())
        {
            case ".swk":
                Editor.NeuesDokument(DokumentSpeicher.Laden(pfad), pfad, false);
                _einstellungen.MerkeZuletzt(pfad);
                _einstellungen.Speichern();
                AktualisiereZuletzt();
                NachDokumentwechsel();
                break;
            case ".odg":
            {
                var ergebnis = OdgImporter.Importieren(pfad);
                Editor.NeuesDokument(ergebnis.Dokument, null, true);
                NachDokumentwechsel();
                ZeigeImportHinweise(pfad, ergebnis, "über das OpenDocument-Format");
                break;
            }
            case ".pub":
                await OeffnePublisherAsync(pfad);
                break;
            default:
                MessageBox.Show(this, "Dieses Dateiformat wird nicht unterstützt. Möglich sind .swk, .pub und .odg.", "Seitenwerk", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
        }
    });

    private async Task OeffnePublisherAsync(string pfad)
    {
        var soffice = PubImport.FindeLibreOffice(_einstellungen.LibreOfficePfad);
        if (soffice == null)
        {
            var antwort = MessageBox.Show(this,
                "Publisher-Dateien (.pub) werden über das kostenlose Programm LibreOffice gelesen. Es wurde auf diesem Computer nicht gefunden.\n\n" +
                "Ja: Download-Seite von LibreOffice öffnen\nNein: Installationsort von LibreOffice selbst auswählen\nAbbrechen: Nichts tun",
                "LibreOffice wird benötigt", MessageBoxButton.YesNoCancel, MessageBoxImage.Information);
            if (antwort == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo(LibreOfficeAdresse) { UseShellExecute = true });
            else if (antwort == MessageBoxResult.No && LibreOfficeWaehlen())
                soffice = PubImport.FindeLibreOffice(_einstellungen.LibreOfficePfad);
            if (soffice == null)
                return;
        }

        var fortschritt = new FortschrittFenster($"„{Path.GetFileName(pfad)}“ wird mit LibreOffice umgewandelt.\nBeim ersten Start kann das eine halbe Minute dauern.") { Owner = this };
        IsEnabled = false;
        fortschritt.Show();
        ImportErgebnis ergebnis;
        try
        {
            ergebnis = await PubImport.ImportierenAsync(pfad, soffice, fortschritt.Abbruch);
        }
        finally
        {
            fortschritt.Schliessen();
            IsEnabled = true;
            Activate();
        }

        Editor.NeuesDokument(ergebnis.Dokument, null, true);
        NachDokumentwechsel();
        ZeigeImportHinweise(pfad, ergebnis, "über LibreOffice");
    }

    private void ZeigeImportHinweise(string pfad, ImportErgebnis ergebnis, string weg)
    {
        Editor.ImportHinweise.Clear();
        Editor.ImportHinweise.AddRange(ergebnis.Hinweise);
        if (App.Selbsttest)
            return; // Im automatischen Test gibt es niemanden, der ein Meldungsfenster schließt.

        var text = new StringBuilder();
        text.AppendLine($"„{Path.GetFileName(pfad)}“ wurde {weg} geöffnet ({ergebnis.Dokument.Seiten.Count} Seite{(ergebnis.Dokument.Seiten.Count == 1 ? "" : "n")}).");
        text.AppendLine();
        text.AppendLine("Das Ergebnis kann vom Original abweichen (Schriftarten, Abstände, Grafiken, Effekte). Bitte alles prüfen. " +
                        "Speichern Sie die Datei danach als Seitenwerk-Dokument (.swk); das Zurückschreiben in das .pub-Format ist nicht möglich.");
        if (ergebnis.Hinweise.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Hinweise zum Import:");
            foreach (var hinweis in ergebnis.Hinweise.Take(12))
                text.AppendLine("• " + hinweis);
        }
        MessageBox.Show(this, text.ToString(), "Datei geöffnet", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private bool LibreOfficeWaehlen()
    {
        var dialog = new OpenFileDialog
        {
            Title = "soffice.exe von LibreOffice auswählen",
            Filter = "LibreOffice (soffice.exe)|soffice.exe|Alle Dateien|*.*",
        };
        if (dialog.ShowDialog(this) != true)
            return false;
        _einstellungen.LibreOfficePfad = dialog.FileName;
        _einstellungen.Speichern();
        ZeigeMeldung("LibreOffice-Pfad gespeichert.");
        return true;
    }

    // ---------------------------------------------------------------- Speichern und Exportieren

    private bool Speichern() => Editor.Pfad == null ? SpeichernUnter() : SchreibeDatei(Editor.Pfad);

    private bool SpeichernUnter()
    {
        Editor.BeendeTextbearbeitung();
        var dialog = new SaveFileDialog
        {
            Title = "Speichern unter",
            Filter = "Seitenwerk-Dokument (*.swk)|*.swk",
            FileName = Path.GetFileNameWithoutExtension(Dokumentname),
            DefaultExt = ".swk",
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(this) == true && SchreibeDatei(dialog.FileName);
    }

    private bool SchreibeDatei(string pfad)
    {
        Editor.BeendeTextbearbeitung();
        Editor.Dokument.Titel = Path.GetFileNameWithoutExtension(pfad);
        DokumentSpeicher.Speichern(Editor.Dokument, pfad);
        Editor.Pfad = pfad;
        Editor.MarkiereAlsGespeichert();
        _einstellungen.MerkeZuletzt(pfad);
        _einstellungen.Speichern();
        AktualisiereZuletzt();
        AktualisiereTitel();
        ZeigeMeldung("Gespeichert.");
        return true;
    }

    private void PdfExportieren()
    {
        Editor.BeendeTextbearbeitung();
        var dialog = new SaveFileDialog
        {
            Title = "Als PDF exportieren",
            Filter = "PDF-Datei (*.pdf)|*.pdf",
            FileName = Path.GetFileNameWithoutExtension(Dokumentname),
            DefaultExt = ".pdf",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true)
            return;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            Editor.Dokument.Titel ??= Path.GetFileNameWithoutExtension(dialog.FileName);
            Exporter.PdfSpeichern(Editor.Dokument, dialog.FileName);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
        ZeigeMeldung("PDF gespeichert: " + Path.GetFileName(dialog.FileName));
    }

    private void PngExportieren()
    {
        Editor.BeendeTextbearbeitung();
        var dialog = new SaveFileDialog
        {
            Title = "Seiten als Bilder exportieren",
            Filter = "PNG-Bild (*.png)|*.png",
            FileName = Path.GetFileNameWithoutExtension(Dokumentname),
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = false,
        };
        if (dialog.ShowDialog(this) != true)
            return;
        Mouse.OverrideCursor = Cursors.Wait;
        List<string> dateien;
        try
        {
            dateien = Exporter.PngSpeichern(Editor.Dokument, Path.GetDirectoryName(dialog.FileName)!, Path.GetFileNameWithoutExtension(dialog.FileName));
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
        ZeigeMeldung(dateien.Count == 1 ? "Bild gespeichert." : $"{dateien.Count} Bilder gespeichert.");
    }

    private void Drucken()
    {
        Editor.BeendeTextbearbeitung();
        Exporter.Drucken(Editor.Dokument, Dokumentname);
    }

    private void ZeigeInfo()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        MessageBox.Show(this,
            $"Seitenwerk {version}\n\nLayoutprogramm für Flyer, Plakate, Einladungen und Broschüren.\n\n" +
            "Eigenes Format: .swk\nÖffnen: .swk, .pub (Microsoft Publisher, über LibreOffice), .odg\nExport: PDF, PNG, Druck\n\n" +
            "Publisher-Dateien werden mit dem Import-Filter von LibreOffice gelesen; dafür muss LibreOffice installiert sein.",
            "Info", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---------------------------------------------------------------- Zuletzt geöffnet

    private void AktualisiereZuletzt()
    {
        ZuletztListe.Items.Clear();
        foreach (var pfad in _einstellungen.Zuletzt)
        {
            var knopf = new Button
            {
                Content = Path.GetFileName(pfad),
                ToolTip = pfad,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 4, 6, 4),
                MinWidth = 220,
                Cursor = Cursors.Hand,
            };
            var datei = pfad;
            knopf.Click += (_, _) =>
            {
                Menueband.ApplicationMenu.IsDropDownOpen = false;
                _ = OeffneDateiAsync(datei);
            };
            ZuletztListe.Items.Add(knopf);
        }
        ZuletztLeer.Visibility = ZuletztListe.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- Schließen

    private void FensterSchliesst(object? sender, CancelEventArgs e)
    {
        Editor.BeendeTextbearbeitung();
        if (!FrageSpeichern())
        {
            e.Cancel = true;
            return;
        }
        _einstellungen.Rechtschreibung = Editor.Rechtschreibung;
        _einstellungen.ZeigeLineale = Editor.ZeigeLineale;
        _einstellungen.ZeigeNavigation = Editor.ZeigeNavigation;
        _einstellungen.ObjektFang = Editor.ObjektFang;
        _einstellungen.Speichern();
    }
}
