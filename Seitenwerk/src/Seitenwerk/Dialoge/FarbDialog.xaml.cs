using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Seitenwerk.Darstellung;

namespace Seitenwerk.Dialoge;

public partial class FarbDialog : Window
{
    private bool _intern;

    public FarbDialog(string? startFarbe)
    {
        InitializeComponent();
        foreach (var hex in Farben.Palette)
        {
            var knopf = new Button
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(1),
                Background = Farben.Pinsel(hex),
                ToolTip = hex,
                Tag = hex,
            };
            knopf.Click += (_, _) => Setze((string)knopf.Tag);
            Palette.Children.Add(knopf);
        }
        Setze(startFarbe ?? "#000000");
    }

    /// <summary>Die gewählte Farbe als "#RRGGBB".</summary>
    public string Farbe { get; private set; } = "#000000";

    private void Setze(string hex)
    {
        var farbe = Farben.Lesen(hex);
        if (farbe == null)
            return;
        _intern = true;
        RotRegler.Value = farbe.Value.R;
        GruenRegler.Value = farbe.Value.G;
        BlauRegler.Value = farbe.Value.B;
        Farbe = $"#{farbe.Value.R:X2}{farbe.Value.G:X2}{farbe.Value.B:X2}";
        HexBox.Text = Farbe;
        Vorschau.Background = new SolidColorBrush(Color.FromRgb(farbe.Value.R, farbe.Value.G, farbe.Value.B));
        _intern = false;
    }

    private void ReglerGeaendert(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_intern || !IsLoaded)
            return;
        Setze($"#{(int)RotRegler.Value:X2}{(int)GruenRegler.Value:X2}{(int)BlauRegler.Value:X2}");
    }

    private void HexGeaendert(object sender, TextChangedEventArgs e)
    {
        if (_intern || !IsLoaded)
            return;
        var text = HexBox.Text.Trim();
        if (!text.StartsWith('#'))
            text = "#" + text;
        if (text.Length == 7 && Farben.Lesen(text) != null)
            Setze(text);
    }

    private void OkKlick(object sender, RoutedEventArgs e) => DialogResult = true;
}
