using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class RoundMatrixView : ContentView
{
    public static readonly BindableProperty PlayersProperty = BindableProperty.Create(
        nameof(Players),
        typeof(IReadOnlyList<PlayerColumnViewModel>),
        typeof(RoundMatrixView),
        propertyChanged: OnPlayersChanged);

    public static readonly BindableProperty TrackCountTextProperty = BindableProperty.Create(
        nameof(TrackCountText),
        typeof(string),
        typeof(RoundMatrixView),
        defaultValue: "16",
        defaultBindingMode: BindingMode.TwoWay);

    public static readonly BindableProperty ColumnHeader0Property = BindableProperty.Create(
        nameof(ColumnHeader0), typeof(View), typeof(RoundMatrixView));

    public static readonly BindableProperty ColumnHeader1Property = BindableProperty.Create(
        nameof(ColumnHeader1), typeof(View), typeof(RoundMatrixView));

    public static readonly BindableProperty ColumnHeader2Property = BindableProperty.Create(
        nameof(ColumnHeader2), typeof(View), typeof(RoundMatrixView));

    public static readonly BindableProperty ColumnHeader3Property = BindableProperty.Create(
        nameof(ColumnHeader3), typeof(View), typeof(RoundMatrixView));

    public static readonly BindableProperty DebugInfoProperty = BindableProperty.Create(
        nameof(DebugInfo),
        typeof(string),
        typeof(RoundMatrixView),
        defaultValue: "[DEBUG MATRIX] Players? = (inte satt ännu)");

    public static readonly BindableProperty DebugInfoVisibleProperty = BindableProperty.Create(
        nameof(DebugInfoVisible),
        typeof(bool),
        typeof(RoundMatrixView),
        defaultValue: true);

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

    public View? ColumnHeader0
    {
        get => (View?)GetValue(ColumnHeader0Property);
        set => SetValue(ColumnHeader0Property, value);
    }

    public View? ColumnHeader1
    {
        get => (View?)GetValue(ColumnHeader1Property);
        set => SetValue(ColumnHeader1Property, value);
    }

    public View? ColumnHeader2
    {
        get => (View?)GetValue(ColumnHeader2Property);
        set => SetValue(ColumnHeader2Property, value);
    }

    public View? ColumnHeader3
    {
        get => (View?)GetValue(ColumnHeader3Property);
        set => SetValue(ColumnHeader3Property, value);
    }

    public string DebugInfo
    {
        get => (string)GetValue(DebugInfoProperty);
        set => SetValue(DebugInfoProperty, value);
    }

    public bool DebugInfoVisible
    {
        get => (bool)GetValue(DebugInfoVisibleProperty);
        set => SetValue(DebugInfoVisibleProperty, value);
    }

    public RoundMatrixView()
    {
        InitializeComponent();
    }

    private static void OnPlayersChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (RoundMatrixView)bindable;
        var list = newValue as IReadOnlyList<PlayerColumnViewModel>;
        if (list is null)
        {
            view.DebugInfo = "[DEBUG MATRIX] Players = null";
            return;
        }
        var summary = string.Join(", ",
            list.Select((p, i) => $"[{i}]'{p.PlayerName}' F={p.FirstPlacesText}/S={p.SecondPlacesText}/T={p.ThirdPlacesText}/Fo={p.FourthPlacesText}"));
        view.DebugInfo = $"[DEBUG MATRIX] Players.Count={list.Count}; {summary}";
    }
}
