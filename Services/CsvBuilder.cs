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
    {
        ArgumentNullException.ThrowIfNull(nights);
        ArgumentNullException.ThrowIfNull(orderedPlayers);
        if (orderedPlayers.Count != 4)
        {
            throw new InvalidOperationException(
                $"Förväntade 4 aktiva spelare, hittade {orderedPlayers.Count}.");
        }

        var ordered = nights
            .Where(n => n.Rounds.Count > 0)
            .OrderBy(n => n.Night.PlayedOn)
            .ToList();
        if (ordered.Count == 0)
        {
            throw new InvalidOperationException("Inga omgångar att exportera ännu.");
        }

        var playerIds = orderedPlayers.Select(p => p.Id).ToList();
        var totalRows = ordered.Count * 9 - 1;
        var grid = CreateGrid(totalRows, TotalColumns);

        FillSection1(grid, ordered, orderedPlayers, playerIds);
        FillSection2(grid, ordered, orderedPlayers, playerIds);
        FillSection3(grid, ordered, orderedPlayers, playerIds);

        return RenderGrid(grid);
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
        IReadOnlyList<NightWithRounds> nights,
        IReadOnlyList<Player> players,
        IReadOnlyList<int> playerIds)
    {
        for (int i = 0; i < nights.Count; i++)
        {
            FillNightBlock(grid, i * 9, i + 1, nights[i], players, playerIds);
        }
    }

    private static void FillNightBlock(
        string[][] grid,
        int rowStart,
        int nightNumber,
        NightWithRounds night,
        IReadOnlyList<Player> players,
        IReadOnlyList<int> playerIds)
    {
        var roundsByNumber = night.Rounds.OrderBy(r => r.Round.RoundNumber).ToList();
        var totalTracks = roundsByNumber.Sum(r => r.Round.TrackCount);
        var date = night.Night.PlayedOn.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        grid[rowStart][Section1Start + 0] = $"Kväll {nightNumber}";
        grid[rowStart][Section1Start + 1] = date;

        for (int p = 0; p < players.Count; p++)
        {
            grid[rowStart + 1][Section1Start + 1 + p] = players[p].Name;
        }
        grid[rowStart + 1][Section1Start + 5] = totalTracks.ToString(CultureInfo.InvariantCulture);

        var sumFirsts = playerIds.ToDictionary(id => id, _ => 0);
        var sumSeconds = playerIds.ToDictionary(id => id, _ => 0);
        var sumThirds = playerIds.ToDictionary(id => id, _ => 0);
        var sumFourths = playerIds.ToDictionary(id => id, _ => 0);
        var sumPoints = playerIds.ToDictionary(id => id, _ => 0);
        var sumTracks = playerIds.ToDictionary(id => id, _ => 0);

        foreach (var round in roundsByNumber)
        {
            var resultsByPlayer = round.Results.ToDictionary(r => r.PlayerId);
            foreach (var id in playerIds)
            {
                if (!resultsByPlayer.TryGetValue(id, out var rr))
                {
                    throw new InvalidOperationException(
                        $"Omgång {round.Round.Id} (kväll {night.Night.Id}) saknar resultat för spelare {id}. Datakorruption misstänks.");
                }
                sumFirsts[id] += rr.FirstPlaces;
                sumSeconds[id] += rr.SecondPlaces;
                sumThirds[id] += rr.ThirdPlaces;
                sumFourths[id] += rr.FourthPlaces;
                sumPoints[id] += StatsCalculator.PointsFor(rr);
                sumTracks[id] += StatsCalculator.TracksFor(rr);
            }
        }

        WriteCountRow(grid, rowStart + 2, "1", sumFirsts, playerIds);
        WriteCountRow(grid, rowStart + 3, "2", sumSeconds, playerIds);
        WriteCountRow(grid, rowStart + 4, "3", sumThirds, playerIds);
        WriteCountRow(grid, rowStart + 5, "4", sumFourths, playerIds);
        WriteCountRow(grid, rowStart + 6, "Poäng", sumPoints, playerIds);

        grid[rowStart + 7][Section1Start + 0] = "Snitt";
        for (int p = 0; p < playerIds.Count; p++)
        {
            var id = playerIds[p];
            if (sumTracks[id] == 0)
            {
                throw new InvalidOperationException(
                    $"Spelare {id} har 0 banor på kväll {night.Night.Id}. Datakorruption misstänks.");
            }
            var avg = (decimal)sumPoints[id] / sumTracks[id];
            grid[rowStart + 7][Section1Start + 1 + p] = avg.ToString("0.00", SvSe);
        }
    }

    private static void WriteCountRow(
        string[][] grid,
        int row,
        string label,
        IReadOnlyDictionary<int, int> values,
        IReadOnlyList<int> playerIds)
    {
        grid[row][Section1Start + 0] = label;
        for (int p = 0; p < playerIds.Count; p++)
        {
            grid[row][Section1Start + 1 + p] = values[playerIds[p]].ToString(CultureInfo.InvariantCulture);
        }
    }

    private static void FillSection2(
        string[][] grid,
        IReadOnlyList<NightWithRounds> nights,
        IReadOnlyList<Player> players,
        IReadOnlyList<int> playerIds)
    {
        grid[0][Section2Start + 0] = "Kväll";
        for (int p = 0; p < players.Count; p++)
        {
            grid[0][Section2Start + 1 + p] = players[p].Name;
        }

        for (int n = 0; n < nights.Count; n++)
        {
            var row = n + 1;
            grid[row][Section2Start + 0] = (n + 1).ToString(CultureInfo.InvariantCulture);

            var perPlayer = playerIds.ToDictionary(id => id, _ => new List<int>());
            foreach (var round in nights[n].Rounds.OrderBy(r => r.Round.RoundNumber).Where(r => r.IsComplete))
            {
                var positions = StatsCalculator.CalculateRoundPositions(round, playerIds);
                foreach (var id in playerIds)
                {
                    perPlayer[id].Add(positions.PositionByPlayer[id]);
                }
            }

            for (int p = 0; p < playerIds.Count; p++)
            {
                var list = perPlayer[playerIds[p]];
                grid[row][Section2Start + 1 + p] = list.Count == 0
                    ? string.Empty
                    : string.Join(",", list.Select(v => v.ToString(CultureInfo.InvariantCulture)));
            }
        }
    }

    private static void FillSection3(
        string[][] grid,
        IReadOnlyList<NightWithRounds> nights,
        IReadOnlyList<Player> players,
        IReadOnlyList<int> playerIds)
    {
        var history = StatsCalculator.CalculateHistory(nights, playerIds);

        grid[0][Section3Start + 0] = "Tot placeringar:";
        for (int p = 0; p < players.Count; p++)
        {
            grid[0][Section3Start + 1 + p] = players[p].Name;
        }

        for (int position = 1; position <= 4; position++)
        {
            grid[position][Section3Start + 0] = position.ToString(CultureInfo.InvariantCulture);
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
                grid[position][Section3Start + 1 + p] = value.ToString(CultureInfo.InvariantCulture);
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
}
