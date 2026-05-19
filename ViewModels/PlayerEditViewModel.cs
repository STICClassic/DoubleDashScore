using System.Collections.ObjectModel;
using System.Diagnostics;
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
        // DIAGNOSTIK Skiva 6c — utreder regression att Spara verkar "göra ingenting"
        // efter att PlayerEditPage öppnas via flyout-menyn. Hypotes: GoToAsync("..")
        // fail:ar tyst eftersom sidan nu är root-ShellContent (öppnad via "//Route")
        // i stället för pushad på en stack ovanpå NightsListPage. Tas bort när fixen
        // är gjord. Skriv ut Shell:s nuvarande Location först så vi vet exakt vad
        // ".."-navigationen försöker göra.
        Debug.WriteLine($"[PlayerEdit] SaveCommand triggered. Location before save: '{Shell.Current?.CurrentState?.Location}'.");
        IsBusy = true;
        try
        {
            Debug.WriteLine($"[PlayerEdit] Items to save: {Items.Count}.");
            foreach (var item in Items)
            {
                if (!string.IsNullOrWhiteSpace(item.Name))
                {
                    Debug.WriteLine($"[PlayerEdit] UpdateNameAsync({item.Id}, '{item.Name.Trim()}')…");
                    await _players.UpdateNameAsync(item.Id, item.Name.Trim()).ConfigureAwait(true);
                }
            }
            Debug.WriteLine("[PlayerEdit] DB-uppdateringar klara. Anropar GoToAsync(\"..\")…");
            try
            {
                await Shell.Current.GoToAsync("..").ConfigureAwait(true);
                Debug.WriteLine($"[PlayerEdit] GoToAsync(\"..\") returnerade utan exception. Ny location: '{Shell.Current?.CurrentState?.Location}'.");
            }
            catch (Exception navEx)
            {
                Debug.WriteLine($"[PlayerEdit] GoToAsync(\"..\") kastade {navEx.GetType().Name}: {navEx.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PlayerEdit] Oväntat undantag i SaveAsync: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            IsBusy = false;
            Debug.WriteLine("[PlayerEdit] SaveAsync finally-block kört, IsBusy=false.");
        }
    }

    [RelayCommand]
    private static async Task CancelAsync()
    {
        // DIAGNOSTIK Skiva 6c — samma misstänkta orsak som Save (GoToAsync("..")
        // mot tom navigation-stack). Tas bort med fixen.
        Debug.WriteLine($"[PlayerEdit] CancelCommand triggered. Location: '{Shell.Current?.CurrentState?.Location}'.");
        try
        {
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            Debug.WriteLine($"[PlayerEdit] Cancel GoToAsync(\"..\") returnerade utan exception. Ny location: '{Shell.Current?.CurrentState?.Location}'.");
        }
        catch (Exception navEx)
        {
            Debug.WriteLine($"[PlayerEdit] Cancel GoToAsync(\"..\") kastade {navEx.GetType().Name}: {navEx.Message}");
        }
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
