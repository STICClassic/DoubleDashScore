namespace DoubleDashScore.Services;

// Kanonisk spelarfärgs-palett (matchar CLAUDE.md "Tema och design").
// ENDA källan till spelarfärger i kod. Både NightsListViewModel (bygger
// MAUI-`Color`) och HistoryStatsViewModel (bygger OxyPlot-`OxyColor`) läser
// härifrån. Lägg inte en till hex-lista någon annanstans — adaptera till
// rätt färgtyp på konsumentsidan i stället.
public static class PlayerColors
{
    public static readonly IReadOnlyDictionary<string, string> HexByName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Claes"]  = "#E55A1F",  // röd-orange
            ["Robin"]  = "#1F77B4",  // blå
            ["Aleksi"] = "#2CA02C",  // grön
            ["Jonas"]  = "#B8860B",  // mörk gul/guld
        };

    // RGB-byte-trippel för en hex-sträng i "#RRGGBB"-form. För konsumenter
    // som bygger en icke-MAUI färgtyp (t.ex. OxyColor) och inte kan parsa
    // hex själva.
    public static (byte R, byte G, byte B) ToRgb(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        var s = hex.TrimStart('#');
        return (
            Convert.ToByte(s.Substring(0, 2), 16),
            Convert.ToByte(s.Substring(2, 2), 16),
            Convert.ToByte(s.Substring(4, 2), 16));
    }
}
