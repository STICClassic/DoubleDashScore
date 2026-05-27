using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Graphics;

namespace DoubleDashScore.ViewModels;

// En spelarrad i den anpassade legenden under grafen. Tap togglar IsVisible
// vilket sätter LineSeries.IsVisible på den underliggande PlotModel:en.
//
// NameOpacity och NameDecorations är derived så att XAML kan binda direkt
// utan converter — opacity dimmar texten när linjen är dold, strikethrough
// gör avvalet tydligt.
public partial class PlayerLegendItem : ObservableObject
{
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
}
