using System.Globalization;
using System.Text;
using DoubleDashScore.Data;
using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

public static class CsvBuilder
{
    private const char Separator = ';';
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
        var sb = new StringBuilder();

        for (int i = 0; i < ordered.Count; i++)
        {
            AppendNightBlock(sb, i + 1, ordered[i], orderedPlayers, playerIds);
            sb.Append('\n');
        }

        AppendNightPlacements(sb, ordered, orderedPlayers, playerIds);
        sb.Append('\n');

        AppendTotalscore(sb, ordered, orderedPlayers, playerIds);

        return sb.ToString();
    }

    private static void AppendNightBlock(
        StringBuilder sb,
        int nightNumber,
        NightWithRounds night,
        IReadOnlyList<Player> players,
        IReadOnlyList<int> playerIds)
    {
        var roundsByNumber = night.Rounds.OrderBy(r => r.Round.RoundNumber).ToList();
        var totalTracks = roundsByNumber.Sum(r => r.Round.TrackCount);
        var date = night.Night.PlayedOn.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        WriteRow(sb, new List<string> { $"Kväll {nightNumber}", date });

        var nameRow = new List<string> { string.Empty };
        nameRow.AddRange(players.Select(p => p.Name));
        nameRow.Add(totalTracks.ToString(CultureInfo.InvariantCulture));
        WriteRow(sb, nameRow);

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

        WriteCountRow(sb, "1", sumFirsts, playerIds);
        WriteCountRow(sb, "2", sumSeconds, playerIds);
        WriteCountRow(sb, "3", sumThirds, playerIds);
        WriteCountRow(sb, "4", sumFourths, playerIds);
        WriteCountRow(sb, "Poäng", sumPoints, playerIds);

        var snittCells = new List<string> { "Snitt" };
        foreach (var id in playerIds)
        {
            var tracks = sumTracks[id];
            if (tracks == 0)
            {
                throw new InvalidOperationException(
                    $"Spelare {id} har 0 banor på kväll {night.Night.Id}. Datakorruption misstänks.");
            }
            var avg = (decimal)sumPoints[id] / tracks;
            snittCells.Add(avg.ToString("0.00", SvSe));
        }
        WriteRow(sb, snittCells);
    }

    private static void WriteCountRow(
        StringBuilder sb,
        string label,
        IReadOnlyDictionary<int, int> values,
        IReadOnlyList<int> playerIds)
    {
        var cells = new List<string> { label };
        foreach (var id in playerIds)
        {
            cells.Add(values[id].ToString(CultureInfo.InvariantCulture));
        }
        WriteRow(sb, cells);
    }

    private static void AppendNightPlacements(
        StringBuilder sb,
        IReadOnlyList<NightWithRounds> nights,
        IReadOnlyList<Player> players,
        IReadOnlyList<int> playerIds)
    {
        var header = new List<string> { "Spelare" };
        for (int i = 0; i < nights.Count; i++)
        {
            header.Add($"Kväll {i + 1}");
        }
        WriteRow(sb, header);

        var placementsPerNightPerPlayer = new List<Dictionary<int, List<int>>>(nights.Count);
        foreach (var night in nights)
        {
            var perPlayer = playerIds.ToDictionary(id => id, _ => new List<int>());
            foreach (var round in night.Rounds.OrderBy(r => r.Round.RoundNumber).Where(r => r.IsComplete))
            {
                var positions = StatsCalculator.CalculateRoundPositions(round, playerIds);
                foreach (var id in playerIds)
                {
                    perPlayer[id].Add(positions.PositionByPlayer[id]);
                }
            }
            placementsPerNightPerPlayer.Add(perPlayer);
        }

        foreach (var player in players)
        {
            var row = new List<string> { player.Name };
            foreach (var nightMap in placementsPerNightPerPlayer)
            {
                var list = nightMap[player.Id];
                row.Add(list.Count == 0
                    ? string.Empty
                    : string.Join(",", list.Select(v => v.ToString(CultureInfo.InvariantCulture))));
            }
            WriteRow(sb, row);
        }
    }

    private static void AppendTotalscore(
        StringBuilder sb,
        IReadOnlyList<NightWithRounds> nights,
        IReadOnlyList<Player> players,
        IReadOnlyList<int> playerIds)
    {
        var history = StatsCalculator.CalculateHistory(nights, playerIds);

        var header = new List<string> { "Tot placeringar:" };
        header.AddRange(players.Select(p => p.Name));
        WriteRow(sb, header);

        for (int position = 1; position <= 4; position++)
        {
            var row = new List<string> { position.ToString(CultureInfo.InvariantCulture) };
            foreach (var id in playerIds)
            {
                var counts = history.PositionTotals.ByPlayer[id];
                var value = position switch
                {
                    1 => counts.Firsts,
                    2 => counts.Seconds,
                    3 => counts.Thirds,
                    4 => counts.Fourths,
                    _ => throw new InvalidOperationException(),
                };
                row.Add(value.ToString(CultureInfo.InvariantCulture));
            }
            WriteRow(sb, row);
        }
    }

    private static void WriteRow(StringBuilder sb, IReadOnlyList<string> cells)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            if (i > 0) sb.Append(Separator);
            sb.Append(Escape(cells[i]));
        }
        sb.Append('\n');
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
