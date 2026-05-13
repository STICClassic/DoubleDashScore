using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

public static class MappingValidator
{
    public static (bool IsValid, string? Error) Validate(IReadOnlyList<Player?> selections)
    {
        if (selections.Count != 4)
        {
            return (false, $"Förväntade 4 val, fick {selections.Count}.");
        }

        for (int i = 0; i < selections.Count; i++)
        {
            if (selections[i] is null)
            {
                return (false, $"Slot P{i + 1} saknar spelare.");
            }
        }

        var duplicate = selections
            .GroupBy(p => p!.Id)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            return (false, $"Spelaren '{duplicate.First()!.Name}' är vald flera gånger.");
        }

        return (true, null);
    }
}
