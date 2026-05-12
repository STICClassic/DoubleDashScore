using System.Globalization;
using System.Text;
using DoubleDashScore.Data;
using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

public static class CsvBuilder
{
    private const char Separator = ';';
    private const int TotalColumns = 18;
    private const int Section1Start = 0;   // Column A
    private const int Section2Start = 7;   // Column H
    private const int Section3Start = 13;  // Column N

    private static readonly CultureInfo SvSe = CultureInfo.GetCultureInfo("sv-SE");

    public static string BuildHistoryCsv(
        IReadOnlyList<NightWithRounds> nights,
        IReadOnlyList<Player> orderedPlayers)
        => BuildHistoryCsv(nights, orderedPlayers, HistoricalSeed.Empty);

    public static string BuildHistoryCsv(
        IReadOnlyList<NightWithRounds> nights,
        IReadOnlyList<Player> orderedPlayers,
        HistoricalSeed seed)
    {
        ArgumentNullException.ThrowIfNull(nights);
        ArgumentNullException.ThrowIfNull(orderedPlayers);
        ArgumentNullException.ThrowIfNull(seed);
        if (orderedPlayers.Count != 4)
        {
            throw new InvalidOperationException(
                $"Förväntade 4 aktiva spelare, hittade {orderedPlayers.Count}.");
        }

        var playerIds = orderedPlayers.Select(p => p.Id).ToList();
        var unified = BuildUnifiedNights(nights, playerIds, seed);
        if (unified.Count == 0)
        {
            throw new InvalidOperationException("Inga omgångar att exportera ännu.");
        }

        // Layout: 2 (title + blank/header) + 9 rows per night (8 used + 1 gap).
        var totalRows = unified.Count * 9 + 1;
        var grid = CreateGrid(totalRows, TotalColumns);

        FillSection1(grid, unified, orderedPlayers, playerIds);
        FillSection2(grid, unified, orderedPlayers, playerIds);
        FillSection3(grid, nights, seed, orderedPlayers, playerIds);

        return RenderGrid(grid);
    }

    private static List<UnifiedNight> BuildUnifiedNights(
        IReadOnlyList<NightWithRounds> appNights,
        IReadOnlyList<int> playerIds,
        HistoricalSeed seed)
    {
        var unified = new List<UnifiedNight>();
        int displayNumber = 0;

        var placementsByKey = seed.RoundPlacements
            .GroupBy(p => (p.NightNumber, p.PlayerId))
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<int>)g.OrderBy(p => p.RoundIndex).Select(p => p.Position).ToList());

        var histByNight = seed.NightAggregates
            .GroupBy(a => a.NightNumber)
            .OrderBy(g => g.Key);

        foreach (var nightGroup in histByNight)
        {
            displayNumber++;
            var nightNumber = nightGroup.Key;
            var aggsByPlayer = nightGroup.ToDictionary(a => a.PlayerId);
            var perPlayer = new Dictionary<int, UnifiedPlayerNight>(playerIds.Count);
            int sharedTotalTracks = 0;

            foreach (var id in playerIds)
            {
                if (!aggsByPlayer.TryGetValue(id, out var agg))
                {
                    throw new InvalidOperationException(
                        $"Historisk kväll {nightNumber} saknar aggregat för spelare {id} — datakorruption misstänks.");
                }
                var tracks = agg.FirstPlaces + agg.SecondPlaces + agg.ThirdPlaces + agg.FourthPlaces;
                var points = 4 * agg.FirstPlaces + 3 * agg.SecondPlaces + 2 * agg.ThirdPlaces + agg.FourthPlaces;
                var placements = placementsByKey.TryGetValue((nightNumber, id), out var list)
                    ? list
                    : (IReadOnlyList<int>)Array.Empty<int>();
                perPlayer[id] = new UnifiedPlayerNight(
                    agg.FirstPlaces, agg.SecondPlaces, agg.ThirdPlaces, agg.FourthPlaces,
                    tracks, points, placements);
                sharedTotalTracks = agg.TotalTracks;
            }
            unified.Add(new UnifiedNight(
                DisplayNumber: displayNumber,
                IsHistorical: true,
                PlayedOnLocal: null,
                TotalTracks: sharedTotalTracks,
                PerPlayer: perPlayer));
        }

        var orderedApp = appNights
            .Where(n => n.Rounds.Count > 0)
            .OrderBy(n => n.Night.PlayedOn);

        foreach (var night in orderedApp)
        {
            displayNumber++;
            var totalTracks = night.Rounds.Sum(r => r.Round.TrackCount);
            var perPlayer = new Dictionary<int, UnifiedPlayerNight>(playerIds.Count);

            foreach (var id in playerIds)
            {
                int f = 0, s = 0, t = 0, fo = 0, points = 0, tracks = 0;
                var placements = new List<int>();
                foreach (var round in night.Rounds.OrderBy(r => r.Round.RoundNumber))
                {
                    var rr = round.Results.FirstOrDefault(r => r.PlayerId == id)
                        ?? throw new InvalidOperationException(
                            $"Omgång {round.Round.Id} (kväll {night.Night.Id}) saknar resultat för spelare {id}. Datakorruption misstänks.");
                    f += rr.FirstPlaces;
                    s += rr.SecondPlaces;
                    t += rr.ThirdPlaces;
                    fo += rr.FourthPlaces;
                    points += StatsCalculator.PointsFor(rr);
                    tracks += StatsCalculator.TracksFor(rr);
                    if (round.IsComplete)
                    {
                        var positions = StatsCalculator.CalculateRoundPositions(round, playerIds);
                        placements.Add(positions.PositionByPlayer[id]);
                    }
                }
                perPlayer[id] = new UnifiedPlayerNight(f, s, t, fo, tracks, points, placements);
            }

            unified.Add(new UnifiedNight(
                DisplayNumber: displayNumber,
                IsHistorical: false,
                PlayedOnLocal: night.Night.PlayedOn.ToLocalTime(),
                TotalTracks: totalTracks,
                PerPlayer: perPlayer));
        }

        return unified;
    }

    private static string[][] CreateGrid(int rows, int cols)
    {
        var grid = new string[rows][];
        for (int i = 0; i < rows; i++)
        {
            grid[i] = new string[cols];
            for (int j = 0; j < cols; j++) grid[i][j] = string.Empty;
        }
        return grid;
    }

    private static void FillSection1(
        string[][] grid,
        IReadOnlyList<UnifiedNight> unified,
        IReadOnlyList<Player> players,
        IReadOnlyList<int> playerIds)
    {
        grid[0][Section1Start + 0] = "KVÄLLSBLOCK";
        for (int i = 0; i < unified.Count; i++)
        {
            FillNightBlock(grid, 2 + i * 9, unified[i], players, playerIds);
        }
    }

    private static void FillNightBlock(
        string[][] grid,
        int rowStart,
        UnifiedNight night,
        IReadOnlyList<Player> players,
        IReadOnlyList<int> playerIds)
    {
        grid[rowStart][Section1Start + 0] = $"Kväll {night.DisplayNumber}";
        // Historiska kvällar har tom datumcell per spec; appkvällar har ISO-datum.
        grid[rowStart][Section1Start + 1] = night.IsHistorical
            ? string.Empty
            : night.PlayedOnLocal!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        for (int p = 0; p < players.Count; p++)
        {
            grid[rowStart + 1][Section1Start + 1 + p] = players[p].Name;
        }
        grid[rowStart + 1][Section1Start + 5] = night.TotalTracks.ToString(CultureInfo.InvariantCulture);

        WriteCountRow(grid, rowStart + 2, "1", night.PerPlayer, playerIds, d => d.Firsts);
        WriteCountRow(grid, rowStart + 3, "2", night.PerPlayer, playerIds, d => d.Seconds);
        WriteCountRow(grid, rowStart + 4, "3", night.PerPlayer, playerIds, d => d.Thirds);
        WriteCountRow(grid, rowStart + 5, "4", night.PerPlayer, playerIds, d => d.Fourths);
        WriteCountRow(grid, rowStart + 6, "Poäng", night.PerPlayer, playerIds, d => d.Points);

        grid[rowStart + 7][Section1Start + 0] = "Snitt";
        for (int p = 0; p < playerIds.Count; p++)
        {
            var d = night.PerPlayer[playerIds[p]];
            if (d.Tracks == 0)
            {
                var label = night.IsHistorical ? "historisk kväll" : "kväll";
                throw new InvalidOperationException(
                    $"Spelare {playerIds[p]} har 0 banor på {label} {night.DisplayNumber}. Datakorruption misstänks.");
            }
            var avg = (decimal)d.Points / d.Tracks;
            grid[rowStart + 7][Section1Start + 1 + p] = avg.ToString("0.00", SvSe);
        }
    }

    private static void WriteCountRow(
        string[][] grid,
        int row,
        string label,
        IReadOnlyDictionary<int, UnifiedPlayerNight> perPlayer,
        IReadOnlyList<int> playerIds,
        Func<UnifiedPlayerNight, int> selector)
    {
        grid[row][Section1Start + 0] = label;
        for (int p = 0; p < playerIds.Count; p++)
        {
            grid[row][Section1Start + 1 + p] = selector(perPlayer[playerIds[p]])
                .ToString(CultureInfo.InvariantCulture);
        }
    }

    private static void FillSection2(
        string[][] grid,
        IReadOnlyList<UnifiedNight> unified,
        IReadOnlyList<Player> players,
        IReadOnlyList<int> playerIds)
    {
        grid[0][Section2Start + 0] = "KVÄLLSPLACERINGAR";
        grid[1][Section2Start + 0] = "Kväll";
        for (int p = 0; p < players.Count; p++)
        {
            grid[1][Section2Start + 1 + p] = players[p].Name;
        }

        for (int n = 0; n < unified.Count; n++)
        {
            var night = unified[n];
            var row = 2 + n;
            grid[row][Section2Start + 0] = night.DisplayNumber.ToString(CultureInfo.InvariantCulture);
            for (int p = 0; p < playerIds.Count; p++)
            {
                var list = night.PerPlayer[playerIds[p]].Placements;
                grid[row][Section2Start + 1 + p] = list.Count == 0
                    ? string.Empty
                    : string.Join(",", list.Select(v => v.ToString(CultureInfo.InvariantCulture)));
            }
        }
    }

    private static void FillSection3(
        string[][] grid,
        IReadOnlyList<NightWithRounds> appNights,
        HistoricalSeed seed,
        IReadOnlyList<Player> players,
        IReadOnlyList<int> playerIds)
    {
        var history = StatsCalculator.CalculateHistory(appNights, playerIds, seed);

        grid[0][Section3Start + 0] = "TOTALSCORE";
        grid[1][Section3Start + 0] = "Tot placeringar:";
        for (int p = 0; p < players.Count; p++)
        {
            grid[1][Section3Start + 1 + p] = players[p].Name;
        }

        for (int position = 1; position <= 4; position++)
        {
            var row = 1 + position;
            grid[row][Section3Start + 0] = position.ToString(CultureInfo.InvariantCulture);
            for (int p = 0; p < playerIds.Count; p++)
            {
                var counts = history.PositionTotals.ByPlayer[playerIds[p]];
                var value = position switch
                {
                    1 => counts.Firsts,
                    2 => counts.Seconds,
                    3 => counts.Thirds,
                    4 => counts.Fourths,
                    _ => throw new InvalidOperationException(),
                };
                grid[row][Section3Start + 1 + p] = value.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    private static string RenderGrid(string[][] grid)
    {
        var sb = new StringBuilder();
        foreach (var row in grid)
        {
            for (int j = 0; j < row.Length; j++)
            {
                if (j > 0) sb.Append(Separator);
                sb.Append(Escape(row[j]));
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.IndexOfAny(new[] { Separator, '"', '\r', '\n' }) < 0)
        {
            return value;
        }
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private sealed record UnifiedNight(
        int DisplayNumber,
        bool IsHistorical,
        DateTime? PlayedOnLocal,
        int TotalTracks,
        IReadOnlyDictionary<int, UnifiedPlayerNight> PerPlayer);

    private sealed record UnifiedPlayerNight(
        int Firsts,
        int Seconds,
        int Thirds,
        int Fourths,
        int Tracks,
        int Points,
        IReadOnlyList<int> Placements);
}
