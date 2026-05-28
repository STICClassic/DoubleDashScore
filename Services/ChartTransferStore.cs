using OxyPlot;
using OxyPlot.Annotations;

namespace DoubleDashScore.Services;

/// Vilken graf som ska visas i helskärm. Sätts av respektive tabbs
/// Helskärm-knapp innan navigering. FullScreenChartViewModel läser
/// store.Active baserat på detta värde — fullscreen-koden är annars
/// grafagnostisk (samma interaktion, auto-hide, markörlinje för båda).
public enum GraphKind
{
    NightAverage,
    CareerAverage,
}

/// Per-graf-state: den specifika PlotModel:en, dess markörlinje, samt
/// listan av kvällar med snittsiffror som legenden använder. Två
/// instanser hålls av ChartTransferStore (en per graf-kind).
public sealed class GraphSlot
{
    public PlotModel? PlotModel { get; set; }

    /// Markörlinjen för vald kväll. Måste ligga i denna slots PlotModel.
    /// Anvanvänds av både HistoryStatsViewModel (bygger den vid LoadAsync)
    /// och FullScreenChartViewModel (togglar färgen för auto-hide).
    public LineAnnotation? MarkerAnnotation { get; set; }

    /// Kvällar i kronologisk ordning + spelarsnitten den kvällen som ska
    /// renderas i legenden. För kvällsgrafen: kvällssnittet. För karriär-
    /// grafen: löpande karriärsnitt (kumulativt medelvärde t.o.m. den
    /// kvällen). SelectedNightIndex indexerar BÅDA listorna eftersom
    /// kvällarna är samma och i samma ordning.
    public IReadOnlyList<NightScrubberSlice> NightSlices { get; set; } =
        Array.Empty<NightScrubberSlice>();
}

/// Singleton som delar graf-state mellan HistoryStatsViewModel (bygger
/// båda grafernas PlotModels + slices vid LoadAsync) och FullScreen-
/// ChartViewModel (visar EN av dem i helskärm). Anledningen till stuget:
/// PlotModel går inte att skicka via Shell-navigations-query-string, och
/// att bygga om modellerna i fullscreen-vyn skulle innebära dubbel repo-
/// laddning + duplicerad BuildPlotModel-kod.
///
/// Skiva 16 utökade detta från en enskild "current"-PlotModel till två
/// slots (NightAverage + CareerAverage). ActiveGraph säger vilken slot
/// fullscreen visar.
public sealed class ChartTransferStore
{
    /// Slot för kvällsgrafen — varje kvälls eget kvällssnitt per spelare.
    public GraphSlot NightAverage { get; } = new();

    /// Slot för karriärgrafen — löpande karriärsnitt per spelare (kumulativt
    /// medelvärde av kvällssnitten t.o.m. respektive kväll).
    public GraphSlot CareerAverage { get; } = new();

    /// Vilken slot helskärm ska rendera. Sätts av tabbens Helskärm-knapp
    /// före Shell-navigeringen; FullScreenChartViewModel läser Active.
    public GraphKind ActiveGraph { get; set; } = GraphKind.NightAverage;

    public GraphSlot Active =>
        ActiveGraph == GraphKind.NightAverage ? NightAverage : CareerAverage;

    /// Delad spelartoggle. Att gömma Jonas i ena grafen gömmer honom
    /// även i den andra — båda visar samma fyra spelare med samma färger,
    /// så användarens "fokusera på Claes vs Robin"-läge är globalt.
    public HashSet<string> HiddenPlayerNames { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// Delad vald kväll. Båda graferna har samma kronologiska kväll-axel
    /// (samma stats.Series), så indexet pekar på "kväll N" i båda slots'
    /// NightSlices-listor. När användaren rör SelectedNightIndex i ena
    /// grafens tab uppdateras markörlinjen i båda PlotModels och
    /// snitt-siffrorna i båda legenderna.
    public int SelectedNightIndex { get; set; } = -1;

    public bool IsVisible(string playerName) =>
        !HiddenPlayerNames.Contains(playerName);

    public bool TogglePlayerVisibility(string playerName)
    {
        if (HiddenPlayerNames.Contains(playerName))
        {
            HiddenPlayerNames.Remove(playerName);
            return true;
        }
        HiddenPlayerNames.Add(playerName);
        return false;
    }

    /// Synkar synligheten i BÅDA slots' PlotModel.Series mot
    /// HiddenPlayerNames. Anropas av båda VM:erna när toggle ändras så
    /// vi inte glömmer den ena grafen. Returnerar antal series som
    /// ändrades (summerat över båda) så caller kan undvika onödig
    /// InvalidatePlot om inget ändrades.
    public int ApplyVisibilityToPlots()
    {
        int changed = 0;
        changed += ApplyToSlot(NightAverage);
        changed += ApplyToSlot(CareerAverage);
        return changed;

        int ApplyToSlot(GraphSlot slot)
        {
            if (slot.PlotModel is null) return 0;
            int c = 0;
            foreach (var series in slot.PlotModel.Series)
            {
                var shouldBeVisible = IsVisible(series.Title);
                if (series.IsVisible != shouldBeVisible)
                {
                    series.IsVisible = shouldBeVisible;
                    c++;
                }
            }
            return c;
        }
    }
}
