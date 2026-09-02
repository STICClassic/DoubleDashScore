using DoubleDashScore.Services;
using Xunit;

namespace DoubleDashScore.Tests;

/// <summary>
/// Villkoret för att en sparad omgång får skriva om den persisterade
/// position-till-spelare-mappningen: ny omgång eller redigering av databasens
/// senaste omgång — aldrig en äldre omgång.
/// </summary>
public class MappingPersistenceRuleTests
{
    [Fact]
    public void NewRound_FromManualEntry_Persists()
    {
        // Manuell inmatning skickar RoundId 0 för en ny omgång.
        Assert.True(MappingPersistenceRule.ShouldPersist(editedRoundId: 0, latestRoundId: 42));
    }

    [Fact]
    public void NewRound_FromOcr_Persists()
    {
        // OCR-flödet redigerar aldrig — det har ingen omgång att skicka in.
        Assert.True(MappingPersistenceRule.ShouldPersist(editedRoundId: null, latestRoundId: 42));
    }

    [Fact]
    public void FirstRoundEver_Persists()
    {
        Assert.True(MappingPersistenceRule.ShouldPersist(editedRoundId: 0, latestRoundId: null));
    }

    [Fact]
    public void EditingLatestRound_Persists()
    {
        Assert.True(MappingPersistenceRule.ShouldPersist(editedRoundId: 42, latestRoundId: 42));
    }

    [Fact]
    public void EditingSecondToLastRound_DoesNotPersist()
    {
        Assert.False(MappingPersistenceRule.ShouldPersist(editedRoundId: 41, latestRoundId: 42));
    }

    [Fact]
    public void EditingRoundFromOldNight_DoesNotPersist()
    {
        Assert.False(MappingPersistenceRule.ShouldPersist(editedRoundId: 3, latestRoundId: 187));
    }

    [Fact]
    public void EditingRound_WithNoLatestKnown_DoesNotPersist()
    {
        // Kan inte hända i praktiken (omgången vi redigerar finns ju), men
        // "vet inte" ska aldrig leda till att dagens mappning skrivs över.
        Assert.False(MappingPersistenceRule.ShouldPersist(editedRoundId: 7, latestRoundId: null));
    }
}
