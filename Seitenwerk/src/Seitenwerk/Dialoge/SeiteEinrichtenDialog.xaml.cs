using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Seitenwerk.Core.Modell;

namespace Seitenwerk.Dialoge;

public partial class SeiteEinrichtenDialog : Window
{
    private static readonly Seitenformat Eigenes = new("Benutzerdefiniert", 0, 0);
    private bool _intern;

    public SeiteEinrichtenDialog(string titel, double breite, double hoehe, Seitenraender raender)
    {
        InitializeComponent();
        Title = titel;

        FormatBox.Items.Add(Eigenes);
        foreach (var format in Seitenformat.Standard)
            FormatBox.Items.Add(format);
        FormatBox.DisplayMemberPath = nameof(Seitenformat.Name);

        _intern = true;
        BreiteBox.Text = Zahl(breite);
        HoeheBox.Text = Zahl(hoehe);
        ObenBox.Text = Zahl(raender.Oben);
        UntenBox.Text = Zahl(raender.Unten);
        LinksBox.Text = Zahl(raender.Links);
        RechtsBox.Text = Zahl(raender.Rechts);
        _intern = false;
        AktualisiereAuswahl();
    }

    public double Breite { get; private set; }
    public double Hoehe { get; private set; }
    public Seitenraender Raender { get; private set; } = new();

    private static string Zahl(double wert) => Math.Round(wert, 2).ToString("0.##", CultureInfo.CurrentCulture);

    private static bool Lies(string text, out double wert)
    {
        text = text.Trim().Replace("mm", "").Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out wert)
               || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out wert);
    }

    private void AktualisiereAuswahl()
    {
        if (!Lies(BreiteBox.Text, out var b) || !Lies(HoeheBox.Text, out var h))
            return;
        _intern = true;
        var passend = Seitenformat.Standard.FirstOrDefault(f =>
            (Math.Abs(f.Breite - b) < 0.6 && Math.Abs(f.Hoehe - h) < 0.6) || (Math.Abs(f.Breite - h) < 0.6 && Math.Abs(f.Hoehe - b) < 0.6));
        FormatBox.SelectedItem = passend ?? Eigenes;
        HochBox.IsChecked = h >= b;
        QuerBox.IsChecked = b > h;
        _intern = false;
    }

    private void FormatGewaehlt(object sender, SelectionChangedEventArgs e)
    {
        if (_intern || FormatBox.SelectedItem is not Seitenformat format || format == Eigenes)
            return;
        var quer = QuerBox.IsChecked == true;
        _intern = true;
        BreiteBox.Text = Zahl(quer ? format.Hoehe : format.Breite);
        HoeheBox.Text = Zahl(quer ? format.Breite : format.Hoehe);
        _intern = false;
    }

    private void MasseGeaendert(object sender, TextChangedEventArgs e)
    {
        if (!_intern && IsLoaded)
            AktualisiereAuswahl();
    }

    private void AusrichtungGewaehlt(object sender, RoutedEventArgs e)
    {
        if (_intern || !IsLoaded || !Lies(BreiteBox.Text, out var b) || !Lies(HoeheBox.Text, out var h))
            return;
        var quer = QuerBox.IsChecked == true;
        if (quer == b > h)
            return;
        _intern = true;
        BreiteBox.Text = Zahl(h);
        HoeheBox.Text = Zahl(b);
        _intern = false;
    }

    private void OkKlick(object sender, RoutedEventArgs e)
    {
        if (!Lies(BreiteBox.Text, out var b) || !Lies(HoeheBox.Text, out var h) || b < 10 || h < 10 || b > 5000 || h > 5000)
        {
            FehlerText.Text = "Breite und Höhe müssen Zahlen zwischen 10 und 5000 Millimetern sein.";
            return;
        }
        if (!Lies(ObenBox.Text, out var oben) || !Lies(UntenBox.Text, out var unten)
            || !Lies(LinksBox.Text, out var links) || !Lies(RechtsBox.Text, out var rechts)
            || oben < 0 || unten < 0 || links < 0 || rechts < 0)
        {
            FehlerText.Text = "Die Ränder müssen Zahlen von 0 oder größer sein.";
            return;
        }
        if (links + rechts >= b || oben + unten >= h)
        {
            FehlerText.Text = "Die Ränder sind größer als die Seite.";
            return;
        }

        Breite = b;
        Hoehe = h;
        Raender = new Seitenraender { Oben = oben, Unten = unten, Links = links, Rechts = rechts };
        DialogResult = true;
    }
}
