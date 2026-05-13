using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Services;

public static class RoundMatrixValidator
{
    public static (bool IsValid, string Message) Validate(
        IReadOnlyList<PlayerColumnViewModel> players,
        string trackCountText)
    {
        if (!int.TryParse(trackCountText, out var trackCount) || trackCount < 1 || trackCount > 16)
        {
            return (false, "Antal banor måste vara mellan 1 och 16.");
        }
        if (players.Count != 4)
        {
            return (false, "Fyra spelare krävs.");
        }

        var problems = new List<string>();
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (!p.TryGetCounts(out var c))
            {
                problems.Add($"{p.PlayerName}: ogiltig siffra.");
                continue;
            }
            var sum = c.first + c.second + c.third + c.fourth;
            if (sum != trackCount)
            {
                problems.Add($"{p.PlayerName}: {sum}/{trackCount}.");
            }
        }

        int firstSum = 0, secondSum = 0, thirdSum = 0, fourthSum = 0;
        foreach (var p in players)
        {
            if (!p.TryGetCounts(out var c)) continue;
            firstSum += c.first;
            secondSum += c.second;
            thirdSum += c.third;
            fourthSum += c.fourth;
        }
        if (firstSum != trackCount) problems.Add($"1:or totalt {firstSum}/{trackCount}.");
        if (secondSum != trackCount) problems.Add($"2:or totalt {secondSum}/{trackCount}.");
        if (thirdSum != trackCount) problems.Add($"3:or totalt {thirdSum}/{trackCount}.");
        if (fourthSum != trackCount) problems.Add($"4:or totalt {fourthSum}/{trackCount}.");

        if (problems.Count == 0)
        {
            return (true, "Klar att spara.");
        }
        return (false, string.Join("  ", problems));
    }
}
