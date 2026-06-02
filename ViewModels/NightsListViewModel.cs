using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DoubleDashScore.Data;
using DoubleDashScore.Services;

namespace DoubleDashScore.ViewModels;

public partial class NightsListViewModel : ObservableObject, IRecipient<DatabaseImportedMessage>
{
    private static readonly CultureInfo SvSe = CultureInfo.GetCultureInfo("sv-SE");

    // Spelarfärger som MAUI-`Color`, adapterade från den centrala paletten
    // (PlayerColors.HexByName). Vinnar-raden renderar varje vinnarnamn i sin
    // spelarfärg. Okända namn faller tillbaka till vit text.
    private static readonly Dictionary<string, Color> PlayerColorsByName =
        PlayerColors.HexByName.ToDictionary(
            kv => kv.Key,
            kv => Color.FromArgb(kv.Value),
            StringComparer.OrdinalIgnoreCase);

    private static readonly Color WinnerFallbackColor = Colors.White;

    // Dämpad färg för separatorer ("," + mellanslag) och "—"-platshållaren
    // (~50 % vit, #80 = 128 i alfa-kanalen).
    private static readonly Color MutedColor = Color.FromArgb("#80FFFFFF");

    private readonly GameNightRepository _nights;
    private readonly PlayerRepository _players;

    public NightsListViewModel(GameNightRepository nights, PlayerRepository players)
    {
        _nights = nights;
        _players = players;
        WeakReferenceMessenger.Default.Register(this);
    }

    // Triggas när AppShellViewModel.ImportDatabaseAsync skickar signalen efter
    // en lyckad .db-import. Marshall till UI-tråden eftersom LoadAsync rör
    // ObservableCollection och anroparen kan vara på vilken tråd som helst.
    public void Receive(DatabaseImportedMessage message)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try { await LoadAsync().ConfigureAwait(true); }
            catch { /* fire-and-forget: nästa OnAppearing laddar om igen */ }
        });
    }

    public ObservableCollection<NightListItem> Items { get; } = new();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isEmpty;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            // Spelarnamn för id→namn-mappning i vinnar-raden. Vinnar-Id:na
            // (och komplett-räkningen) kommer färdiga från repository:t.
            var activePlayers = await _players.GetActivePlayersAsync(ct).ConfigureAwait(true);
            var nameById = activePlayers.ToDictionary(p => p.Id, p => p.Name);

            var summaries = await _nights.GetSummariesAsync(ct).ConfigureAwait(true);
            Items.Clear();
            foreach (var s in summaries)
            {
                Items.Add(new NightListItem(
                    s.Night.Id,
                    s.Night.PlayedOn.ToLocalTime().ToString("d MMMM yyyy", SvSe),
                    s.Night.Note ?? string.Empty,
                    FormatRoundCount(s.RoundCount, s.CompleteRoundCount),
                    BuildWinnersText(s.WinnersByRound, nameById)));
            }
            IsEmpty = Items.Count == 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static async Task NewNightAsync()
    {
        await Shell.Current.GoToAsync("NewNightPage").ConfigureAwait(true);
    }

    [RelayCommand]
    private static async Task OpenNightAsync(NightListItem? item)
    {
        if (item is null) return;
        await Shell.Current.GoToAsync($"NightDetailPage?nightId={item.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteNightAsync(NightListItem? item)
    {
        if (item is null) return;
        var page = Shell.Current.CurrentPage;
        var confirm = await page.DisplayAlertAsync(
            "Ta bort kväll?",
            $"Är du säker på att du vill ta bort kväll {item.Date}?",
            "Ja",
            "Avbryt").ConfigureAwait(true);
        if (!confirm) return;
        await _nights.SoftDeleteAsync(item.Id).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    private static string FormatRoundCount(int total, int complete)
    {
        if (total == 0) return "Inga omgångar";
        return $"{total} omgång{(total == 1 ? "" : "ar")} ({complete} komplett{(complete == 1 ? "" : "a")})";
    }

    // Bygger en komma-separerad rad över vinnarna i varje komplett omgång,
    // i kronologisk omgångsordning (winnersByRound kommer redan ordnad och
    // partiella omgångar är redan bortfiltrerade av repository:t). Varje
    // vinnarnamn får sin spelarfärg och separatorerna en dämpad färg.
    // Saknas kompletta omgångar visas en dämpad "—".
    //
    // Vid delad förstaplats (tie) innehåller en omgångs inre lista flera
    // spelare; de listas åtskilda med "/", var och en i sin spelarfärg.
    private static FormattedString BuildWinnersText(
        IReadOnlyList<IReadOnlyList<int>> winnersByRound,
        IReadOnlyDictionary<int, string> nameById)
    {
        var fs = new FormattedString();
        if (winnersByRound.Count == 0)
        {
            fs.Spans.Add(new Span { Text = "—", TextColor = MutedColor });
            return fs;
        }

        for (int g = 0; g < winnersByRound.Count; g++)
        {
            if (g > 0)
            {
                fs.Spans.Add(new Span { Text = ", ", TextColor = MutedColor });
            }
            var group = winnersByRound[g];
            for (int w = 0; w < group.Count; w++)
            {
                if (w > 0)
                {
                    fs.Spans.Add(new Span { Text = "/", TextColor = MutedColor });
                }
                var name = nameById.TryGetValue(group[w], out var n) ? n : $"#{group[w]}";
                fs.Spans.Add(new Span
                {
                    Text = name,
                    TextColor = ColorForName(name),
                    FontFamily = "Baloo2Medium",
                });
            }
        }
        return fs;
    }

    private static Color ColorForName(string name) =>
        PlayerColorsByName.TryGetValue(name, out var c) ? c : WinnerFallbackColor;
}

public sealed record NightListItem(
    int Id,
    string Date,
    string Note,
    string RoundsSummary,
    FormattedString Winners);
