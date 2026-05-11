using System.Text;
using DoubleDashScore.Data;

namespace DoubleDashScore.Services;

public sealed class ExportService
{
    private readonly GameNightRepository _nights;
    private readonly PlayerRepository _players;

    public ExportService(GameNightRepository nights, PlayerRepository players)
    {
        _nights = nights;
        _players = players;
    }

    public async Task<string> ExportAllNightsToCsvAsync(CancellationToken ct = default)
    {
        var players = await _players.GetActivePlayersAsync(ct).ConfigureAwait(false);
        if (players.Count != 4)
        {
            throw new InvalidOperationException(
                $"Förväntade 4 aktiva spelare, hittade {players.Count}.");
        }

        var nights = await _nights.GetAllNightsWithRoundsAsync(ct).ConfigureAwait(false);
        var csv = CsvBuilder.BuildHistoryCsv(nights, players);

        var fileName = $"mariokart-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.csv";
        var path = Path.Combine(FileSystem.Current.CacheDirectory, fileName);
        await File.WriteAllTextAsync(path, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct)
            .ConfigureAwait(false);
        return path;
    }
}
