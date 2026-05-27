namespace DoubleDashScore.Services;

// En "skiva" för en kväll — alla fyra spelares kvällssnitt, keyed på
// spelarnamn så att PlayerLegendItem (som inte känner till PlayerId)
// kan slå upp direkt. Lever i Services snarare än ViewModels eftersom
// ChartTransferStore (i Services) håller listan av slices, och tester
// behöver kunna referera typen utan att dra in MAUI-deps.
public sealed record NightScrubberSlice(
    int ChronologicalIndex,
    string DateLabel,
    IReadOnlyDictionary<string, decimal> AverageByPlayerName);
