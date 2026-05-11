using System.Globalization;
using System.Text;
using DoubleDashScore.Data;
using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

public static class CsvBuilder
{
    private const char Separator = ';';

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
        AppendHeader(sb, orderedPlayers);

        for (int n = 0; n < ordered.Count; n++)
        {
            var night = ordered[n];
            var nightNumber = n + 1;
            foreach (var round in night.Rounds.OrderBy(r => r.Round.RoundNumber))
            {
                AppendRoundRow(sb, night.Night, nightNumber, round, playerIds);
            }
        }

        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, IReadOnlyList<Player> players)
    {
        var cols = new List<string>
        {
            "Datum",
            "Kvällsnummer",
            "Kvällsanteckning",
            "Omgång",
            "Banor",
            "Komplett",
        };
        foreach (var p in players)
        {
            cols.Add($"{p.Name} 1:or");
            cols.Add($"{p.Name} 2:or");
            cols.Add($"{p.Name} 3:or");
            cols.Add($"{p.Name} 4:or");
        }
        foreach (var p in players)
        {
            cols.Add($"{p.Name} Poäng");
            cols.Add($"{p.Name} Placering");
        }
        WriteRow(sb, cols);
    }

    private static void AppendRoundRow(
        StringBuilder sb,
        GameNight night,
        int nightNumber,
        RoundDetail round,
        IReadOnlyList<int> playerIds)
    {
        var resultsByPlayer = round.Results.ToDictionary(r => r.PlayerId);
        foreach (var id in playerIds)
        {
            if (!resultsByPlayer.ContainsKey(id))
            {
                throw new InvalidOperationException(
                    $"Omgång {round.Round.Id} (kväll {night.Id}) saknar resultat för spelare {id}. Datakorruption misstänks.");
            }
        }

        var cols = new List<string>
        {
            night.PlayedOn.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            nightNumber.ToString(CultureInfo.InvariantCulture),
            night.Note ?? string.Empty,
            round.Round.RoundNumber.ToString(CultureInfo.InvariantCulture),
            round.Round.TrackCount.ToString(CultureInfo.InvariantCulture),
            round.IsComplete ? "Ja" : "Nej",
        };

        foreach (var id in playerIds)
        {
            var rr = resultsByPlayer[id];
            cols.Add(rr.FirstPlaces.ToString(CultureInfo.InvariantCulture));
            cols.Add(rr.SecondPlaces.ToString(CultureInfo.InvariantCulture));
            cols.Add(rr.ThirdPlaces.ToString(CultureInfo.InvariantCulture));
            cols.Add(rr.FourthPlaces.ToString(CultureInfo.InvariantCulture));
        }

        Dictionary<int, int>? positions = null;
        if (round.IsComplete)
        {
            positions = StatsCalculator.CalculateRoundPositions(round, playerIds)
                .PositionByPlayer.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        foreach (var id in playerIds)
        {
            var rr = resultsByPlayer[id];
            cols.Add(StatsCalculator.PointsFor(rr).ToString(CultureInfo.InvariantCulture));
            cols.Add(positions is null
                ? string.Empty
                : positions[id].ToString(CultureInfo.InvariantCulture));
        }

        WriteRow(sb, cols);
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
