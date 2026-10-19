using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Seitenwerk.Werkzeuge;

namespace Seitenwerk;

public partial class App : Application
{
    public static bool Selbsttest { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += UnerwarteterFehler;

        var args = e.Args;
        if (args.Length >= 1 && args[0] == "--selbsttest")
        {
            Selbsttest = true;
            var ordner = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "seitenwerk-selbsttest");
            Werkzeuge.Selbsttest.Starten(this, ordner);
            return;
        }

        var datei = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && File.Exists(a));
        new MainWindow(null, datei).Show();
    }

    private void UnerwarteterFehler(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var protokoll = ProtokollSchreiben(e.Exception);
        if (Selbsttest)
        {
            // Im Selbsttest wird ein unerwarteter Fehler zum Testfehler; die Ablaufsteuerung meldet ihn.
            Werkzeuge.Selbsttest.MeldeFehler(e.Exception);
            e.Handled = true;
            return;
        }

        e.Handled = true;
        MessageBox.Show(
            "Es ist ein unerwarteter Fehler aufgetreten:\n\n" + e.Exception.Message +
            "\n\nSie können weiterarbeiten. Speichern Sie Ihre Arbeit vorsichtshalber unter einem neuen Namen." +
            (protokoll != null ? "\n\nEinzelheiten stehen in:\n" + protokoll : ""),
            "Seitenwerk", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string? ProtokollSchreiben(Exception ex)
    {
        try
        {
            var ordner = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Seitenwerk");
            Directory.CreateDirectory(ordner);
            var datei = Path.Combine(ordner, "fehler.log");
            File.AppendAllText(datei, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}", Encoding.UTF8);
            return datei;
        }
        catch (Exception schreibFehler) when (schreibFehler is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
