using DoubleDashScore.Models;
using DoubleDashScore.Services;
using Xunit;

namespace DoubleDashScore.Tests;

public class PlayerSlotMapperTests
{
    private static Player P(int id, string name, int order) =>
        new() { Id = id, Name = name, DisplayOrder = order };

    [Fact]
    public void AllFourDefaultsMatch_ReturnsCanonicalOrderRegardlessOfDisplayOrder()
    {
        var players = new[]
        {
            P(1, "Jonas", 0),
            P(2, "Aleksi", 1),
            P(3, "Robin", 2),
            P(4, "Claes", 3),
        };

        var mapped = PlayerSlotMapper.Map(players);

        Assert.Equal("Claes", mapped[0].Name);
        Assert.Equal("Robin", mapped[1].Name);
        Assert.Equal("Aleksi", mapped[2].Name);
        Assert.Equal("Jonas", mapped[3].Name);
    }

    [Fact]
    public void DefaultsMatchCaseInsensitively()
    {
        var players = new[]
        {
            P(1, "CLAES", 0),
            P(2, "robin", 1),
            P(3, "Aleksi", 2),
            P(4, "JONAS", 3),
        };

        var mapped = PlayerSlotMapper.Map(players);

        Assert.Equal("CLAES", mapped[0].Name);
        Assert.Equal("robin", mapped[1].Name);
        Assert.Equal("Aleksi", mapped[2].Name);
        Assert.Equal("JONAS", mapped[3].Name);
    }

    [Fact]
    public void OneRenamed_FallsBackToDisplayOrderForAll()
    {
        var players = new[]
        {
            P(1, "Claes", 3),
            P(2, "Robban", 0),
            P(3, "Aleksi", 1),
            P(4, "Jonas", 2),
        };

        var mapped = PlayerSlotMapper.Map(players);

        Assert.Equal(new[] { "Robban", "Aleksi", "Jonas", "Claes" },
            mapped.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void NoneMatch_AllDisplayOrder()
    {
        var players = new[]
        {
            P(1, "Alice", 2),
            P(2, "Bob", 0),
            P(3, "Carol", 3),
            P(4, "Dave", 1),
        };

        var mapped = PlayerSlotMapper.Map(players);

        Assert.Equal(new[] { "Bob", "Dave", "Alice", "Carol" },
            mapped.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void WrongNumberOfPlayers_Throws()
    {
        var players = new[]
        {
            P(1, "Claes", 0),
            P(2, "Robin", 1),
            P(3, "Aleksi", 2),
        };

        Assert.Throws<ArgumentException>(() => PlayerSlotMapper.Map(players));
    }
}
