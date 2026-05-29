using System.Collections;
using System.Windows.Input;

namespace DoubleDashScore.Views;

public partial class TotalscoreTable : ContentView
{
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(TotalscoreTable));

    public static readonly BindableProperty IsCareerVisibleProperty = BindableProperty.Create(
        nameof(IsCareerVisible),
        typeof(bool),
        typeof(TotalscoreTable),
        defaultValue: false);

    public static readonly BindableProperty ToggleLabelProperty = BindableProperty.Create(
        nameof(ToggleLabel),
        typeof(string),
        typeof(TotalscoreTable),
        defaultValue: string.Empty);

    public static readonly BindableProperty ToggleCommandProperty = BindableProperty.Create(
        nameof(ToggleCommand),
        typeof(ICommand),
        typeof(TotalscoreTable));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public bool IsCareerVisible
    {
        get => (bool)GetValue(IsCareerVisibleProperty);
        set => SetValue(IsCareerVisibleProperty, value);
    }

    public string ToggleLabel
    {
        get => (string)GetValue(ToggleLabelProperty);
        set => SetValue(ToggleLabelProperty, value);
    }

    public ICommand? ToggleCommand
    {
        get => (ICommand?)GetValue(ToggleCommandProperty);
        set => SetValue(ToggleCommandProperty, value);
    }

    public TotalscoreTable()
    {
        InitializeComponent();
    }
}
