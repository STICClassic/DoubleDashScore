using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Graphics;

namespace DoubleDashScore.ViewModels;

// En spelarrad i den anpassade legenden under grafen. Tap togglar IsVisible
// vilket sätter LineSeries.IsVisible på den underliggande PlotModel:en.
//
// NameOpacity och NameDecorations är derived så att XAML kan binda direkt
// utan converter — opacity dimmar texten när linjen är dold, strikethrough
// gör avvalet tydligt.
//
// NightAverage uppdateras när användaren tap:ar en punkt i grafen
// (TrackerChanged i HistoryStatsViewModel/FullScreenChartViewModel) och visas
// under spelarnamnet. Dimmas via NameOpacity för dolda spelare så layouten
// inte hoppar.
public partial class PlayerLegendItem : ObservableObject
{
    private static readonly CultureInfo SvSe = CultureInfo.GetCultureInfo("sv-SE");

    public PlayerLegendItem(string name, Color color, bool isVisible)
    {
        Name = name;
        Color = color;
        _isVisible = isVisible;
    }

    public string Name { get; }

    public Color Color { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameOpacity))]
    [NotifyPropertyChangedFor(nameof(NameDecorations))]
    private bool _isVisible;

    public double NameOpacity => IsVisible ? 1.0 : 0.4;

    public TextDecorations NameDecorations =>
        IsVisible ? TextDecorations.None : TextDecorations.Strikethrough;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NightAverageDisplay))]
    private decimal? _nightAverage;

    public string NightAverageDisplay =>
        NightAverage is { } avg ? avg.ToString("0.00", SvSe) : "—";
}
