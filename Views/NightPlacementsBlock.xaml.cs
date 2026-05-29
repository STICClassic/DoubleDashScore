namespace DoubleDashScore.Views;

public partial class NightPlacementsBlock : ContentView
{
    public static readonly BindableProperty DateLabelProperty = BindableProperty.Create(
        nameof(DateLabel), typeof(string), typeof(NightPlacementsBlock), defaultValue: string.Empty);

    public static readonly BindableProperty Player1Property = BindableProperty.Create(
        nameof(Player1), typeof(string), typeof(NightPlacementsBlock), defaultValue: string.Empty);

    public static readonly BindableProperty Player2Property = BindableProperty.Create(
        nameof(Player2), typeof(string), typeof(NightPlacementsBlock), defaultValue: string.Empty);

    public static readonly BindableProperty Player3Property = BindableProperty.Create(
        nameof(Player3), typeof(string), typeof(NightPlacementsBlock), defaultValue: string.Empty);

    public static readonly BindableProperty Player4Property = BindableProperty.Create(
        nameof(Player4), typeof(string), typeof(NightPlacementsBlock), defaultValue: string.Empty);

    public string DateLabel { get => (string)GetValue(DateLabelProperty); set => SetValue(DateLabelProperty, value); }
    public string Player1 { get => (string)GetValue(Player1Property); set => SetValue(Player1Property, value); }
    public string Player2 { get => (string)GetValue(Player2Property); set => SetValue(Player2Property, value); }
    public string Player3 { get => (string)GetValue(Player3Property); set => SetValue(Player3Property, value); }
    public string Player4 { get => (string)GetValue(Player4Property); set => SetValue(Player4Property, value); }

    public NightPlacementsBlock()
    {
        InitializeComponent();
    }
}
