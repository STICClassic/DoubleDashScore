using System.ComponentModel;
using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class HistoryStatsPage : ContentPage
{
    private readonly HistoryStatsViewModel _vm;

    public HistoryStatsPage(HistoryStatsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HistoryStatsViewModel.SelectedTabIndex)) return;
        if (!_vm.IsPlacementsTab) return;
        if (_vm.PlacementsRows.Count == 0) return;

        // Dispatcha så att CollectionView hinner bli layoutad efter IsVisible-bytet
        // innan vi försöker scrolla. Annars hamnar ScrollTo i tomma luften.
        Dispatcher.Dispatch(() =>
        {
            var last = _vm.PlacementsRows[_vm.PlacementsRows.Count - 1];
            PlacementsCollectionView.ScrollTo(last, position: ScrollToPosition.End, animate: false);
        });
    }
}
