using System.Windows.Input;
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

    /// <summary>
    /// Kommando som körs när ett spelarnamn i rubrikraden tap:as. Parametern är
    /// kolumnens <see cref="PlayerColumnViewModel"/>. Null = raden är inte
    /// interaktiv.
    /// </summary>
    public static readonly BindableProperty NameTapCommandProperty = BindableProperty.Create(
        nameof(NameTapCommand),
        typeof(ICommand),
        typeof(RoundMatrixView));

    /// <summary>Visar en liten pil efter varje namn som affordans för att raden går att tappa.</summary>
    public static readonly BindableProperty IsNamePickerEnabledProperty = BindableProperty.Create(
        nameof(IsNamePickerEnabled),
        typeof(bool),
        typeof(RoundMatrixView),
        defaultValue: false);

    public ICommand? NameTapCommand
    {
        get => (ICommand?)GetValue(NameTapCommandProperty);
        set => SetValue(NameTapCommandProperty, value);
    }

    public bool IsNamePickerEnabled
    {
        get => (bool)GetValue(IsNamePickerEnabledProperty);
        set => SetValue(IsNamePickerEnabledProperty, value);
    }

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
}
