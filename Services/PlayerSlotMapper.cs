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

    /// <summary>
    /// Mappning att öppna OCR-förhandsgranskningen med: den sparade från förra
    /// scanningen om den fortfarande går att applicera, annars <see cref="Map"/>:s
    /// namnbaserade default. Sparade Id:n som inte längre finns bland de aktiva
    /// spelarna (raderad/omdöpt spelare) gör hela mappningen ogiltig — halvt
    /// applicerad mappning vore värre än en känd default.
    /// </summary>
    public static IReadOnlyList<Player> Resolve(
        IReadOnlyList<Player> activePlayers, IReadOnlyList<int>? savedPlayerIds)
    {
        var fallback = Map(activePlayers);
        if (savedPlayerIds is null || savedPlayerIds.Count != activePlayers.Count) return fallback;

        var resolved = new Player[savedPlayerIds.Count];
        for (int i = 0; i < savedPlayerIds.Count; i++)
        {
            var match = activePlayers.FirstOrDefault(p => p.Id == savedPlayerIds[i]);
            if (match is null) return fallback;
            resolved[i] = match;
        }

        return resolved;
    }

    /// <summary>
    /// Sätter <paramref name="chosen"/> på position <paramref name="slotIndex"/>.
    /// Satt spelaren redan på en annan position byter de två plats — mappningen
    /// förblir alltid en permutation, så validering aldrig kan flagga dubbletter.
    /// </summary>
    public static IReadOnlyList<Player?> Assign(
        IReadOnlyList<Player?> current, int slotIndex, Player chosen)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        if (slotIndex < 0 || slotIndex >= current.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        }

        var next = current.ToArray();
        if (next[slotIndex]?.Id == chosen.Id) return next;

        var occupiedBy = Array.FindIndex(next, p => p?.Id == chosen.Id);
        var displaced = next[slotIndex];
        next[slotIndex] = chosen;
        if (occupiedBy >= 0) next[occupiedBy] = displaced;

        return next;
    }
}
