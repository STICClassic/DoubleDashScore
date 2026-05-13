using System.Text.RegularExpressions;
using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

public static class OcrParser
{
    private const int SlotCount = 4;
    private const int RowsPerSlot = 4;
    private const int MaxTrackCount = 16;

    private static readonly Regex DigitOnly = new(@"^\d{1,2}$", RegexOptions.Compiled);

    public static ParsedCounters Parse(OcrResult result)
    {
        var warnings = new List<string>();

        if (result.ImageWidth <= 0)
        {
            warnings.Add("Bildbredd okänd — kan inte sortera kolumner.");
            return BuildEmpty(warnings);
        }

        var digitTokens = result.Tokens
            .Where(t => DigitOnly.IsMatch(t.Text))
            .Select(t => (Token: t, Value: int.Parse(t.Text, System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

        var filteredOut = digitTokens.RemoveAll(t => t.Value > MaxTrackCount);
        if (filteredOut > 0)
        {
            warnings.Add($"{filteredOut} siffra/-or utanför 0–{MaxTrackCount} ignorerades.");
        }

        if (digitTokens.Count == 0)
        {
            warnings.Add("OCR hittade inga siffror i bilden.");
            return BuildEmpty(warnings);
        }

        var byColumn = new List<(OcrToken Token, int Value)>[SlotCount];
        for (int i = 0; i < SlotCount; i++) byColumn[i] = new();

        var quartile = result.ImageWidth / (double)SlotCount;
        foreach (var entry in digitTokens)
        {
            var cx = entry.Token.Box.CenterX;
            var col = (int)Math.Floor(cx / quartile);
            if (col < 0) col = 0;
            if (col >= SlotCount) col = SlotCount - 1;
            byColumn[col].Add(entry);
        }

        var slots = new List<PlayerSlotCounters>(SlotCount);
        for (int col = 0; col < SlotCount; col++)
        {
            var counts = SortAndTakeFour(byColumn[col], col, warnings);
            slots.Add(new PlayerSlotCounters(col, counts[0], counts[1], counts[2], counts[3]));
        }

        var inferred = slots[0].Sum;
        if (slots.Any(s => s.Sum != inferred))
        {
            var sums = string.Join(", ", slots.Select((s, i) => $"P{i + 1}={s.Sum}"));
            warnings.Add($"Kolumnsummorna är inte lika ({sums}). Verifiera räknarna och antal banor.");
        }

        return new ParsedCounters(slots, inferred, warnings);
    }

    private static int[] SortAndTakeFour(
        List<(OcrToken Token, int Value)> column,
        int columnIndex,
        List<string> warnings)
    {
        if (column.Count == 0)
        {
            warnings.Add($"Kolumn P{columnIndex + 1}: inga siffror hittades.");
            return new int[RowsPerSlot];
        }

        var ordered = column
            .OrderBy(t => t.Token.Box.CenterY)
            .Select(t => t.Value)
            .ToList();

        if (ordered.Count < RowsPerSlot)
        {
            warnings.Add($"Kolumn P{columnIndex + 1}: hittade {ordered.Count} siffror, förväntade {RowsPerSlot}.");
            while (ordered.Count < RowsPerSlot) ordered.Add(0);
        }
        else if (ordered.Count > RowsPerSlot)
        {
            warnings.Add($"Kolumn P{columnIndex + 1}: hittade {ordered.Count} siffror, förväntade {RowsPerSlot}. Behåller de fyra översta.");
        }

        return ordered.Take(RowsPerSlot).ToArray();
    }

    private static ParsedCounters BuildEmpty(List<string> warnings)
    {
        var slots = Enumerable.Range(0, SlotCount)
            .Select(i => new PlayerSlotCounters(i, 0, 0, 0, 0))
            .ToList();
        return new ParsedCounters(slots, 0, warnings);
    }
}
