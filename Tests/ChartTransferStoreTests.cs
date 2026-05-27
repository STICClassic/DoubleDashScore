using DoubleDashScore.Services;
using Xunit;

namespace DoubleDashScore.Tests;

public class ChartTransferStoreTests
{
    [Fact]
    public void IsVisible_DefaultsTrue()
    {
        var store = new ChartTransferStore();

        Assert.True(store.IsVisible("Claes"));
        Assert.True(store.IsVisible("Robin"));
        Assert.Empty(store.HiddenPlayerNames);
    }

    [Fact]
    public void TogglePlayerVisibility_HidesPlayer_OnFirstCall()
    {
        var store = new ChartTransferStore();

        var nowVisible = store.TogglePlayerVisibility("Robin");

        Assert.False(nowVisible);
        Assert.False(store.IsVisible("Robin"));
        Assert.Contains("Robin", store.HiddenPlayerNames);
    }

    [Fact]
    public void TogglePlayerVisibility_RestoresPlayer_OnSecondCall()
    {
        var store = new ChartTransferStore();

        store.TogglePlayerVisibility("Robin");
        var nowVisible = store.TogglePlayerVisibility("Robin");

        Assert.True(nowVisible);
        Assert.True(store.IsVisible("Robin"));
        Assert.DoesNotContain("Robin", store.HiddenPlayerNames);
    }

    [Fact]
    public void TogglePlayerVisibility_PlayersAreIndependent()
    {
        var store = new ChartTransferStore();

        store.TogglePlayerVisibility("Claes");
        store.TogglePlayerVisibility("Aleksi");
        store.TogglePlayerVisibility("Jonas");

        Assert.False(store.IsVisible("Claes"));
        Assert.False(store.IsVisible("Aleksi"));
        Assert.False(store.IsVisible("Jonas"));
        Assert.True(store.IsVisible("Robin"));
    }

    [Fact]
    public void TogglePlayerVisibility_IsCaseInsensitive()
    {
        var store = new ChartTransferStore();

        store.TogglePlayerVisibility("Robin");

        Assert.False(store.IsVisible("ROBIN"));
        Assert.False(store.IsVisible("robin"));
        // Andra togglet med annan casing ska behandla som samma spelare.
        var nowVisible = store.TogglePlayerVisibility("robin");
        Assert.True(nowVisible);
        Assert.True(store.IsVisible("Robin"));
    }

    [Fact]
    public void ApplyVisibilityToPlot_WithNullModel_ReturnsZero()
    {
        var store = new ChartTransferStore
        {
            CurrentPlotModel = null,
        };

        var changed = store.ApplyVisibilityToPlot();

        Assert.Equal(0, changed);
    }

    [Fact]
    public void AllFourPlayers_CanBeHiddenIndependently()
    {
        var store = new ChartTransferStore();
        var players = new[] { "Claes", "Robin", "Aleksi", "Jonas" };

        foreach (var p in players)
        {
            store.TogglePlayerVisibility(p);
        }

        Assert.Equal(4, store.HiddenPlayerNames.Count);
        foreach (var p in players)
        {
            Assert.False(store.IsVisible(p));
        }
    }
}
