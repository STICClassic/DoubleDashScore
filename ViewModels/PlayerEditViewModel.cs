using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoubleDashScore.Data;

namespace DoubleDashScore.ViewModels;

public partial class PlayerEditViewModel : ObservableObject
{
    private readonly PlayerRepository _players;

    public PlayerEditViewModel(PlayerRepository players)
    {
        _players = players;
    }

    public ObservableCollection<PlayerEditItem> Items { get; } = new();

    [ObservableProperty]
    private bool _isBusy;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            var players = await _players.GetActivePlayersAsync(ct).ConfigureAwait(true);
            Items.Clear();
            foreach (var p in players)
            {
                Items.Add(new PlayerEditItem(p.Id, p.Name));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            foreach (var item in Items)
            {
                if (!string.IsNullOrWhiteSpace(item.Name))
                {
                    await _players.UpdateNameAsync(item.Id, item.Name.Trim()).ConfigureAwait(true);
                }
            }
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static async Task CancelAsync()
    {
        await Shell.Current.GoToAsync("..").ConfigureAwait(true);
    }
}

public partial class PlayerEditItem : ObservableObject
{
    public PlayerEditItem(int id, string name)
    {
        Id = id;
        _name = name;
    }

    public int Id { get; }

    [ObservableProperty]
    private string _name;
}
