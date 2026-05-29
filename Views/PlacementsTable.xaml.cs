using System.Collections;
using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class PlacementsTable : ContentView
{
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(PlacementsTable));

    public static readonly BindableProperty HeadersProperty = BindableProperty.Create(
        nameof(Headers),
        typeof(PlacementHeaders),
        typeof(PlacementsTable),
        defaultValue: new PlacementHeaders(string.Empty, string.Empty, string.Empty, string.Empty));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public PlacementHeaders Headers
    {
        get => (PlacementHeaders)GetValue(HeadersProperty);
        set => SetValue(HeadersProperty, value);
    }

    public PlacementsTable()
    {
        InitializeComponent();
    }

    // Skrollar interna CollectionView till botten — används av HistoryStats-
    // Page för att auto-scrolla till senaste kvällen när Placeringar-tabben
    // aktiveras. Inkapslar referensen till InnerCollectionView så ägande
    // sida inte behöver veta något om hur tabellen är uppbyggd internt.
    public void ScrollToEnd(object item)
    {
        InnerCollectionView.ScrollTo(item, position: ScrollToPosition.End, animate: false);
    }
}
