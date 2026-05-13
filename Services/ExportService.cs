using System.Text;
using DoubleDashScore.Data;

namespace DoubleDashScore.Services;

public sealed class ExportService
{
    private readonly GameNightRepository _nights;
    private readonly PlayerRepository _players;
    private readonly HistoricalDataRepository _historical;

    public ExportService(
        GameNightRepository nights,
        PlayerRepository players,
        HistoricalDataRepository historical)
    {
        _nights = nights;
        _players = players;
        _historical = historical;
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
        var seed = await _historical.GetSeedAsync(ct).ConfigureAwait(false);
        var csv = CsvBuilder.BuildHistoryCsv(nights, players, seed);

        var fileName = $"mariokart-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.csv";
        var path = Path.Combine(FileSystem.Current.CacheDirectory, fileName);
        await File.WriteAllTextAsync(path, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct)
            .ConfigureAwait(false);
        return path;
    }

    public async Task<string> ExportDatabaseAsync(CancellationToken ct = default)
    {
        var source = DatabaseService.DatabasePath;
        if (!File.Exists(source))
        {
            throw new InvalidOperationException(
                "Databasfilen hittades inte. Starta om appen och försök igen.");
        }

        var fileName = $"doubledashscore-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.db";
        var destination = Path.Combine(FileSystem.Current.CacheDirectory, fileName);

        await using (var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        await using (var dst = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await src.CopyToAsync(dst, ct).ConfigureAwait(false);
        }

        return destination;
    }
}
