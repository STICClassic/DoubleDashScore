using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

public static class PlayerSlotMapper
{
    public static readonly IReadOnlyList<string> DefaultSlotNames =
        new[] { "Claes", "Robin", "Aleksi", "Jonas" };

    public static IReadOnlyList<Player> Map(IReadOnlyList<Player> activePlayers)
    {
        if (activePlayers.Count != 4)
        {
            throw new ArgumentException(
                $"Förväntade exakt 4 aktiva spelare, fick {activePlayers.Count}.",
                nameof(activePlayers));
        }

        var matches = new Player?[4];
        for (int i = 0; i < 4; i++)
        {
            matches[i] = activePlayers.FirstOrDefault(p =>
                string.Equals(p.Name, DefaultSlotNames[i], StringComparison.OrdinalIgnoreCase));
        }

        if (matches.Any(m => m is null))
        {
            return activePlayers
                .OrderBy(p => p.DisplayOrder)
                .ToList();
        }

        return matches.Select(m => m!).ToList();
    }
}
