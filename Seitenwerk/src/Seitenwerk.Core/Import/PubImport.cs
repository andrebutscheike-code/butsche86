using System.Diagnostics;
using Seitenwerk.Core.Modell;

namespace Seitenwerk.Core.Import;

/// <summary>Öffnet Publisher-Dateien (.pub): LibreOffice (Import-Filter libmspub) wandelt sie in eine Zeichnung (.odg) um, die <see cref="OdgImporter"/> einliest.</summary>
public static class PubImport
{
    public const string UmgebungsVariable = "SEITENWERK_SOFFICE";

    /// <summary>Sucht soffice(.exe). <paramref name="gespeichert"/> ist ein vom Benutzer gewählter Pfad und hat Vorrang.</summary>
    public static string? FindeLibreOffice(string? gespeichert = null)
    {
        foreach (var kandidat in Kandidaten(gespeichert))
        {
            if (!string.IsNullOrWhiteSpace(kandidat) && File.Exists(kandidat))
                return kandidat;
        }
        return null;
    }

    private static IEnumerable<string?> Kandidaten(string? gespeichert)
    {
        yield return gespeichert;
        yield return Environment.GetEnvironmentVariable(UmgebungsVariable);

        var dateinamen = OperatingSystem.IsWindows() ? new[] { "soffice.exe" } : new[] { "soffice", "libreoffice" };
        var pfad = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var ordner in pfad.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in dateinamen)
            {
                string? voll = null;
                try
                {
                    voll = Path.Combine(ordner.Trim('"'), name);
                }
                catch (ArgumentException)
                {
                }
                yield return voll;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var basis in new[]
                     {
                         Environment.GetEnvironmentVariable("ProgramFiles"),
                         Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                         Environment.GetEnvironmentVariable("ProgramW6432"),
                         @"C:\Program Files",
                         @"C:\Program Files (x86)",
                     })
            {
                if (string.IsNullOrEmpty(basis) || !Directory.Exists(basis))
                    continue;
                string[] ordnerListe;
                try
                {
                    ordnerListe = Directory.GetDirectories(basis, "LibreOffice*");
                }
                catch (IOException)
                {
                    continue;
                }
                foreach (var ordner in ordnerListe.OrderByDescending(o => o))
                    yield return Path.Combine(ordner, "program", "soffice.exe");
            }
        }
        else
        {
            yield return "/usr/bin/soffice";
            yield return "/usr/local/bin/soffice";
            yield return "/opt/libreoffice/program/soffice";
            yield return "/Applications/LibreOffice.app/Contents/MacOS/soffice";
        }
    }

    public static async Task<ImportErgebnis> ImportierenAsync(string pubPfad, string sofficePfad, CancellationToken abbruch = default)
    {
        if (!File.Exists(pubPfad))
            throw new ImportFehler($"Die Datei „{pubPfad}“ wurde nicht gefunden.");

        var arbeitsOrdner = Path.Combine(Path.GetTempPath(), "Seitenwerk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(arbeitsOrdner);
        try
        {
            var odg = await NachOdgAsync(pubPfad, sofficePfad, arbeitsOrdner, abbruch).ConfigureAwait(false);
            var ergebnis = OdgImporter.Importieren(odg, ausPublisher: Path.GetExtension(pubPfad).Equals(".pub", StringComparison.OrdinalIgnoreCase));
            ergebnis.Dokument.Titel = Path.GetFileNameWithoutExtension(pubPfad);
            return ergebnis;
        }
        finally
        {
            try
            {
                Directory.Delete(arbeitsOrdner, recursive: true);
            }
            catch (IOException)
            {
                // Temporäre Dateien dürfen liegen bleiben, wenn LibreOffice sie noch festhält.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Wandelt eine beliebige von LibreOffice lesbare Datei in .odg um und gibt den Pfad der Ergebnisdatei zurück.</summary>
    public static async Task<string> NachOdgAsync(string quelle, string sofficePfad, string arbeitsOrdner, CancellationToken abbruch = default, int zeitlimitSekunden = 300)
    {
        var profil = Path.Combine(arbeitsOrdner, "profil");
        var ausgabe = Path.Combine(arbeitsOrdner, "ausgabe");
        Directory.CreateDirectory(ausgabe);

        var start = new ProcessStartInfo
        {
            FileName = sofficePfad,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // Eigenes Profil: Eine bereits geöffnete LibreOffice-Sitzung des Benutzers blockiert die Umwandlung sonst.
        foreach (var arg in new[]
                 {
                     "--headless", "--norestore", "--nologo", "--nodefault", "--nolockcheck",
                     "-env:UserInstallation=" + new Uri(profil + Path.DirectorySeparatorChar).AbsoluteUri,
                     "--convert-to", "odg", "--outdir", ausgabe, quelle,
                 })
            start.ArgumentList.Add(arg);

        using var prozess = new Process { StartInfo = start };
        try
        {
            prozess.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ImportFehler("LibreOffice konnte nicht gestartet werden: " + ex.Message, ex);
        }

        var ausgabeText = prozess.StandardOutput.ReadToEndAsync();
        var fehlerText = prozess.StandardError.ReadToEndAsync();

        using var zeitlimit = CancellationTokenSource.CreateLinkedTokenSource(abbruch);
        zeitlimit.CancelAfter(TimeSpan.FromSeconds(zeitlimitSekunden));
        try
        {
            await prozess.WaitForExitAsync(zeitlimit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                prozess.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            if (abbruch.IsCancellationRequested)
                throw;
            throw new ImportFehler($"LibreOffice hat nach {zeitlimitSekunden} Sekunden nicht geantwortet.");
        }

        await Task.WhenAll(ausgabeText, fehlerText).ConfigureAwait(false);

        var ergebnis = Directory.GetFiles(ausgabe, "*.odg").FirstOrDefault();
        if (ergebnis == null)
        {
            var meldung = (await fehlerText.ConfigureAwait(false) + " " + await ausgabeText.ConfigureAwait(false)).Trim();
            throw new ImportFehler("LibreOffice konnte die Datei nicht umwandeln." + (meldung.Length > 0 ? " " + meldung : ""));
        }
        return ergebnis;
    }
}
