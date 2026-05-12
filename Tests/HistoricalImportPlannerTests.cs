using DoubleDashScore.Data;
using DoubleDashScore.Models;
using Xunit;

namespace DoubleDashScore.Tests;

public class HistoricalImportPlannerTests
{
    [Fact]
    public void Plan_OverwriteFalse_ExistingNightsAreSkipped()
    {
        var parsed = MakeParsed(
            nights: new[] { 1, 2, 3 },
            placementsPerNight: new[] { 1, 2, 3 });
        var existing = new HashSet<int> { 1, 2 };

        var plan = HistoricalImportPlanner.Plan(parsed, existing, overwrite: false);

        Assert.Empty(plan.NightNumbersToReplace);
        // Bara kväll 3 ska in (4 aggregat = 4 spelare).
        Assert.Equal(4, plan.NightsInsertedAggregatesOf(3).Count());
        Assert.Empty(plan.NightsInsertedAggregatesOf(1));
        Assert.Empty(plan.NightsInsertedAggregatesOf(2));
        Assert.Equal(1, plan.NightsInserted);
        Assert.Equal(0, plan.NightsOverwritten);
        Assert.Equal(2, plan.NightsSkipped);
    }

    [Fact]
    public void Plan_OverwriteTrue_ExistingNightsAreReplaced()
    {
        var parsed = MakeParsed(
            nights: new[] { 1, 2, 3 },
            placementsPerNight: new[] { 1, 2, 3 });
        var existing = new HashSet<int> { 1, 2 };

        var plan = HistoricalImportPlanner.Plan(parsed, existing, overwrite: true);

        Assert.Equal(new[] { 1, 2 }, plan.NightNumbersToReplace);
        // Alla tre kvällar ska in (1+2 efter delete, 3 som ny).
        Assert.Equal(3, plan.AggregatesToInsert.Select(a => a.NightNumber).Distinct().Count());
        Assert.Equal(1, plan.NightsInserted);
        Assert.Equal(2, plan.NightsOverwritten);
        Assert.Equal(0, plan.NightsSkipped);
    }

    [Fact]
    public void Plan_OverwriteTrue_NightsInDbButNotInFile_AreLeftAlone()
    {
        // Filen har bara kväll 1. DB har kvällar 1, 2, 3.
        // Med overwrite: kväll 1 ersätts. Kvällar 2 och 3 nämns inte i plannen
        // och rör inte raderas av repository:t.
        var parsed = MakeParsed(
            nights: new[] { 1 },
            placementsPerNight: new[] { 1 });
        var existing = new HashSet<int> { 1, 2, 3 };

        var plan = HistoricalImportPlanner.Plan(parsed, existing, overwrite: true);

        Assert.Equal(new[] { 1 }, plan.NightNumbersToReplace);
        Assert.DoesNotContain(2, plan.NightNumbersToReplace);
        Assert.DoesNotContain(3, plan.NightNumbersToReplace);
        Assert.Equal(0, plan.NightsInserted);
        Assert.Equal(1, plan.NightsOverwritten);
    }

    [Fact]
    public void Plan_OverwriteFalse_EmptyDb_AllInserted()
    {
        var parsed = MakeParsed(
            nights: new[] { 1, 2 },
            placementsPerNight: new[] { 1, 2 });
        var existing = new HashSet<int>();

        var plan = HistoricalImportPlanner.Plan(parsed, existing, overwrite: false);

        Assert.Empty(plan.NightNumbersToReplace);
        Assert.Equal(2, plan.NightsInserted);
        Assert.Equal(0, plan.NightsOverwritten);
        Assert.Equal(0, plan.NightsSkipped);
    }

    [Fact]
    public void Plan_OverwriteTrue_FullyIdempotent_WhenFileMatchesDb()
    {
        var parsed = MakeParsed(
            nights: new[] { 1, 2 },
            placementsPerNight: new[] { 1, 2 });
        var existing = new HashSet<int> { 1, 2 };

        var plan = HistoricalImportPlanner.Plan(parsed, existing, overwrite: true);

        Assert.Equal(new[] { 1, 2 }, plan.NightNumbersToReplace);
        Assert.Equal(0, plan.NightsInserted);
        Assert.Equal(2, plan.NightsOverwritten);
    }

    [Fact]
    public void Plan_PlacementsAreFilteredToNightsBeingWritten()
    {
        // Kväll 1 och 2 finns i DB. overwrite=false → bara kväll 3 skrivs.
        // Placements för kväll 1 och 2 ska INTE finnas i plannens lista.
        var parsed = MakeParsed(
            nights: new[] { 1, 2, 3 },
            placementsPerNight: new[] { 2, 2, 2 });
        var existing = new HashSet<int> { 1, 2 };

        var plan = HistoricalImportPlanner.Plan(parsed, existing, overwrite: false);

        Assert.All(plan.PlacementsToInsert, p => Assert.Equal(3, p.NightNumber));
        // Kväll 3 har 2 omgångar × 4 spelare = 8 placeringar.
        Assert.Equal(8, plan.PlacementsToInsert.Count);
    }

    private static ParsedExcelImport MakeParsed(int[] nights, int[] placementsPerNight)
    {
        var aggregates = new List<HistoricalNightAggregate>();
        var placements = new List<HistoricalRoundPlacement>();
        for (int i = 0; i < nights.Length; i++)
        {
            var n = nights[i];
            // Fyra spelare per natt
            for (int playerId = 1; playerId <= 4; playerId++)
            {
                aggregates.Add(new HistoricalNightAggregate
                {
                    NightNumber = n,
                    PlayerId = playerId,
                    FirstPlaces = 4, SecondPlaces = 4, ThirdPlaces = 4, FourthPlaces = 4,
                    TotalTracks = 16,
                    CreatedAt = DateTime.UtcNow,
                });
            }
            for (int r = 1; r <= placementsPerNight[i]; r++)
            {
                for (int playerId = 1; playerId <= 4; playerId++)
                {
                    placements.Add(new HistoricalRoundPlacement
                    {
                        NightNumber = n,
                        PlayerId = playerId,
                        RoundIndex = r,
                        Position = ((playerId - 1) % 4) + 1,
                        CreatedAt = DateTime.UtcNow,
                    });
                }
            }
        }
        return new ParsedExcelImport(
            PlayerNamesInExcelColumnOrder: new[] { "A", "B", "C", "D" },
            NightAggregates: aggregates,
            RoundPlacements: placements,
            PositionTotalsSnapshot: Array.Empty<HistoricalPositionTotalsSnapshot>());
    }
}

internal static class PlanTestExtensions
{
    public static IEnumerable<HistoricalNightAggregate> NightsInsertedAggregatesOf(this HistoricalImportPlan plan, int nightNumber)
        => plan.AggregatesToInsert.Where(a => a.NightNumber == nightNumber);
}
