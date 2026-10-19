using System.Windows;

namespace Seitenwerk.Dialoge;

/// <summary>Kleines Fenster für länger dauernde Aufgaben (z. B. die Umwandlung einer .pub-Datei durch LibreOffice).</summary>
public partial class FortschrittFenster : Window
{
    private readonly CancellationTokenSource _abbruch = new();

    public FortschrittFenster(string meldung)
    {
        InitializeComponent();
        Meldung.Text = meldung;
        Closing += (_, e) =>
        {
            if (!_beendet)
            {
                e.Cancel = true;
                _abbruch.Cancel();
            }
        };
    }

    private bool _beendet;

    public CancellationToken Abbruch => _abbruch.Token;

    public void Schliessen()
    {
        _beendet = true;
        Close();
    }

    private void AbbrechenKlick(object sender, RoutedEventArgs e)
    {
        _abbruch.Cancel();
        AbbrechenKnopf.IsEnabled = false;
        Meldung.Text = "Wird abgebrochen …";
    }
}
