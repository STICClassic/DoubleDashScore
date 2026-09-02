using DoubleDashScore.Models;
using DoubleDashScore.Services;
using Xunit;

namespace DoubleDashScore.Tests;

/// <summary>
/// Vilken källa som styr namn-headern i manuell inmatning: den globala
/// mappningen för en ny omgång, omgångens egna resultatrader vid redigering.
/// </summary>
public class RoundSlotOrderTests
{
    private static readonly Player Claes = P(1, "Claes", 1);
    private static readonly Player Robin = P(2, "Robin", 2);
    private static readonly Player Aleksi = P(3, "Aleksi", 3);
    private static readonly Player Jonas = P(4, "Jonas", 4);

    private static IReadOnlyList<Player> Active() => new[] { Claes, Robin, Aleksi, Jonas };

    private static Player P(int id, string name, int order) =>
        new() { Id = id, Name = name, DisplayOrder = order, CreatedAt = DateTime.UtcNow };

    /// <summary>En omgångs rader i kolumnordning P1-P4, Id stigande som vid insert.</summary>
    private static List<RoundResult> Rows(int firstId, params int[] playerIdsInSlotOrder) =>
        playerIdsInSlotOrder
            .Select((playerId, i) => new RoundResult
            {
                Id = firstId + i,
                RoundId = 7,
                PlayerId = playerId,
                FirstPlaces = 4,
                SecondPlaces = 4,
                ThirdPlaces = 4,
                FourthPlaces = 4,
                CreatedAt = DateTime.UtcNow,
            })
            .ToList();

    private static IReadOnlyList<string> HeaderNames(
        IReadOnlyList<RoundResult>? existingResults, IReadOnlyList<int>? globalMapping)
    {
        var slotIds = RoundSlotOrder.SlotIdsFor(existingResults, globalMapping);
        return PlayerSlotMapper.Resolve(Active(), slotIds).Select(p => p.Name).ToList();
    }

    // --- De fyra scenarierna ---------------------------------------------

    [Fact]
    public void ExistingRound_NeverEdited_ShowsCreationOrder()
    {
        // Skapad med Jonas på P1, medan den globala mappningen säger något annat.
        var rows = Rows(100, Jonas.Id, Claes.Id, Robin.Id, Aleksi.Id);
        var global = new[] { Claes.Id, Robin.Id, Aleksi.Id, Jonas.Id };

        Assert.Equal(
            new[] { "Jonas", "Claes", "Robin", "Aleksi" },
            HeaderNames(rows, global));
    }

    [Fact]
    public void ExistingRound_PreviouslyEditedWithSwap_ShowsSwappedOrder()
    {
        // UpdateRoundAsync mjukraderar de gamla raderna och infogar nya med
        // högre Id i den nya kolumnordningen. Bara de levande raderna räknas.
        var rows = Rows(100, Claes.Id, Robin.Id, Aleksi.Id, Jonas.Id);
        foreach (var r in rows) r.DeletedAt = DateTime.UtcNow;
        rows.AddRange(Rows(200, Robin.Id, Claes.Id, Aleksi.Id, Jonas.Id));

        Assert.Equal(
            new[] { "Robin", "Claes", "Aleksi", "Jonas" },
            HeaderNames(rows, globalMapping: null));
    }

    [Fact]
    public void NewRound_UsesGlobalMapping()
    {
        var global = new[] { Aleksi.Id, Jonas.Id, Claes.Id, Robin.Id };

        Assert.Equal(
            new[] { "Aleksi", "Jonas", "Claes", "Robin" },
            HeaderNames(existingResults: null, global));
    }

    [Fact]
    public void ExistingRound_ReopenedAfterSwapAndSave_ShowsSavedOrder()
    {
        // Byte i headern -> SaveAsync skriver inputs i kolumnordning -> nästa
        // öppning härleder samma ordning ur de nysparade raderna.
        var savedAfterSwap = Rows(300, Aleksi.Id, Robin.Id, Claes.Id, Jonas.Id);

        Assert.Equal(
            new[] { "Aleksi", "Robin", "Claes", "Jonas" },
            HeaderNames(savedAfterSwap, globalMapping: new[] { Claes.Id, Robin.Id, Aleksi.Id, Jonas.Id }));
    }

    // --- Härledningen och dess skyddsnät ----------------------------------

    [Fact]
    public void FromResults_OrdersByRowId_NotListOrder()
    {
        var rows = Rows(100, Claes.Id, Robin.Id, Aleksi.Id, Jonas.Id);
        rows.Reverse();

        Assert.Equal(
            new[] { Claes.Id, Robin.Id, Aleksi.Id, Jonas.Id },
            RoundSlotOrder.FromResults(rows));
    }

    [Fact]
    public void FromResults_NullForNewRound()
    {
        Assert.Null(RoundSlotOrder.FromResults(null));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void FromResults_RejectsWrongRowCount(int rowCount)
    {
        var ids = new[] { Claes.Id, Robin.Id, Aleksi.Id, Jonas.Id, 5 }.Take(rowCount).ToArray();

        Assert.Null(RoundSlotOrder.FromResults(Rows(100, ids)));
    }

    [Fact]
    public void FromResults_RejectsDuplicatePlayer()
    {
        var rows = Rows(100, Claes.Id, Claes.Id, Aleksi.Id, Jonas.Id);

        Assert.Null(RoundSlotOrder.FromResults(rows));
    }

    [Fact]
    public void CorruptRound_FallsBackToNameDefault()
    {
        // Tre levande rader: härledningen förkastas och headern visar
        // namn-defaulten i stället för en påhittad ordning.
        var rows = Rows(100, Jonas.Id, Claes.Id, Robin.Id);

        Assert.Equal(
            new[] { "Claes", "Robin", "Aleksi", "Jonas" },
            HeaderNames(rows, globalMapping: null));
    }

    [Fact]
    public void ExistingRound_WithPlayerNoLongerActive_FallsBackToNameDefault()
    {
        var rows = Rows(100, Claes.Id, Robin.Id, Aleksi.Id, 99);

        Assert.Equal(
            new[] { "Claes", "Robin", "Aleksi", "Jonas" },
            HeaderNames(rows, globalMapping: null));
    }
}
