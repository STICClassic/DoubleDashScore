using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class RoundMatrixView : ContentView
{
    public static readonly BindableProperty PlayersProperty = BindableProperty.Create(
        nameof(Players),
        typeof(IReadOnlyList<PlayerColumnViewModel>),
        typeof(RoundMatrixView));

    public static readonly BindableProperty TrackCountTextProperty = BindableProperty.Create(
        nameof(TrackCountText),
        typeof(string),
        typeof(RoundMatrixView),
        defaultValue: "16",
        defaultBindingMode: BindingMode.TwoWay);

    public IReadOnlyList<PlayerColumnViewModel>? Players
    {
        get => (IReadOnlyList<PlayerColumnViewModel>?)GetValue(PlayersProperty);
        set => SetValue(PlayersProperty, value);
    }

    public string TrackCountText
    {
        get => (string)GetValue(TrackCountTextProperty);
        set => SetValue(TrackCountTextProperty, value);
    }

    public RoundMatrixView()
    {
        InitializeComponent();
    }

    private void OnEntryFocused(object? sender, FocusEventArgs e)
    {
        if (sender is not Entry entry) return;
        var length = entry.Text?.Length ?? 0;
        if (length == 0) return;
        entry.CursorPosition = 0;
        entry.SelectionLength = length;
    }
}
