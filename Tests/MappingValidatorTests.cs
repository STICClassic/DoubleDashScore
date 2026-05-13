using DoubleDashScore.Models;
using DoubleDashScore.Services;
using Xunit;

namespace DoubleDashScore.Tests;

public class MappingValidatorTests
{
    private static Player P(int id, string name) =>
        new() { Id = id, Name = name, DisplayOrder = id - 1 };

    [Fact]
    public void AllFourUnique_IsValid()
    {
        var sel = new Player?[] { P(1, "Claes"), P(2, "Robin"), P(3, "Aleksi"), P(4, "Jonas") };
        var (isValid, error) = MappingValidator.Validate(sel);

        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void DuplicatePlayer_IsInvalid_AndNamesTheDuplicate()
    {
        var claes = P(1, "Claes");
        var sel = new Player?[] { claes, P(2, "Robin"), claes, P(4, "Jonas") };
        var (isValid, error) = MappingValidator.Validate(sel);

        Assert.False(isValid);
        Assert.Contains("Claes", error);
        Assert.Contains("flera", error);
    }

    [Fact]
    public void NullSlot_IsInvalid_AndIdentifiesSlot()
    {
        var sel = new Player?[] { P(1, "Claes"), null, P(3, "Aleksi"), P(4, "Jonas") };
        var (isValid, error) = MappingValidator.Validate(sel);

        Assert.False(isValid);
        Assert.Contains("P2", error);
    }

    [Fact]
    public void ThreeSamePlayer_IsInvalid()
    {
        var robin = P(2, "Robin");
        var sel = new Player?[] { robin, robin, robin, P(4, "Jonas") };
        var (isValid, error) = MappingValidator.Validate(sel);

        Assert.False(isValid);
        Assert.Contains("Robin", error);
    }

    [Fact]
    public void WrongCount_IsInvalid()
    {
        var sel = new Player?[] { P(1, "Claes"), P(2, "Robin"), P(3, "Aleksi") };
        var (isValid, error) = MappingValidator.Validate(sel);

        Assert.False(isValid);
        Assert.Contains("4", error);
    }
}
