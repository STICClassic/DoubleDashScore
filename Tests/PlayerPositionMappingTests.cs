using DoubleDashScore.Models;
using DoubleDashScore.Services;
using Xunit;

namespace DoubleDashScore.Tests;

/// <summary>
/// Skiva 29: position-till-spelare-mappningen, delad av OCR-förhandsgranskningen
/// och manuell inmatning — persistens (codec), återläsning (Resolve) och
/// auto-swap (Assign).
/// </summary>
public class PlayerPositionMappingTests
{
    private static Player P(int id, string name, int order) =>
        new() { Id = id, Name = name, DisplayOrder = order };

    private static IReadOnlyList<Player> Four() => new[]
    {
        P(1, "Claes", 0),
        P(2, "Robin", 1),
        P(3, "Aleksi", 2),
        P(4, "Jonas", 3),
    };

    // --- PlayerPositionMappingCodec ------------------------------------------------

    [Fact]
    public void Codec_RoundTrip_BevararOrdning()
    {
        var json = PlayerPositionMappingCodec.Serialize(new[] { 3, 1, 4, 2 });

        var ids = PlayerPositionMappingCodec.TryDeserialize(json);

        Assert.NotNull(ids);
        Assert.Equal(new[] { 3, 1, 4, 2 }, ids!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("inte json")]
    [InlineData("{\"a\":1}")]
    [InlineData("[1,2,3]")]          // för få
    [InlineData("[1,2,3,4,5]")]      // för många
    [InlineData("[1,2,2,4]")]        // dubblett
    [InlineData("[0,2,3,4]")]        // ogiltigt Id
    [InlineData("[-1,2,3,4]")]
    public void Codec_OgiltigInput_GerNull(string? json)
    {
        Assert.Null(PlayerPositionMappingCodec.TryDeserialize(json));
    }

    // --- PlayerSlotMapper.Resolve ---------------------------------------

    [Fact]
    public void Resolve_UtanSparadMappning_GerNamnbaseradDefault()
    {
        var mapping = PlayerSlotMapper.Resolve(Four(), null);

        Assert.Equal(new[] { "Claes", "Robin", "Aleksi", "Jonas" },
            mapping.Select(p => p.Name));
    }

    [Fact]
    public void Resolve_SparadMappning_Tillampas()
    {
        // Claes satt på P2 förra gången.
        var mapping = PlayerSlotMapper.Resolve(Four(), new[] { 2, 1, 3, 4 });

        Assert.Equal(new[] { "Robin", "Claes", "Aleksi", "Jonas" },
            mapping.Select(p => p.Name));
    }

    [Fact]
    public void Resolve_SparatIdSomInteFinnsLangre_FallerTillbakaHelt()
    {
        // Spelare 9 finns inte bland de aktiva — halvt applicerad mappning
        // vore värre än en känd default.
        var mapping = PlayerSlotMapper.Resolve(Four(), new[] { 9, 1, 3, 4 });

        Assert.Equal(new[] { "Claes", "Robin", "Aleksi", "Jonas" },
            mapping.Select(p => p.Name));
    }

    [Fact]
    public void Resolve_FelAntalSparadeIdn_FallerTillbaka()
    {
        var mapping = PlayerSlotMapper.Resolve(Four(), new[] { 2, 1 });

        Assert.Equal(new[] { "Claes", "Robin", "Aleksi", "Jonas" },
            mapping.Select(p => p.Name));
    }

    // --- PlayerSlotMapper.Assign (auto-swap) ----------------------------

    [Fact]
    public void Assign_ValdSpelareSitterAnnanstans_BytPlats()
    {
        var current = Four().Cast<Player?>().ToList();

        // Tap på P1 (Claes), välj Robin (sitter på P2).
        var next = PlayerSlotMapper.Assign(current, 0, current[1]!);

        Assert.Equal("Robin", next[0]!.Name);
        Assert.Equal("Claes", next[1]!.Name);
        Assert.Equal("Aleksi", next[2]!.Name);
        Assert.Equal("Jonas", next[3]!.Name);
    }

    [Fact]
    public void Assign_ValdSpelareRedanPaPositionen_AndrarIngenting()
    {
        var current = Four().Cast<Player?>().ToList();

        var next = PlayerSlotMapper.Assign(current, 2, current[2]!);

        Assert.Equal(new[] { "Claes", "Robin", "Aleksi", "Jonas" },
            next.Select(p => p!.Name));
    }

    [Fact]
    public void Assign_ForblirAlltidEnPermutation()
    {
        IReadOnlyList<Player?> current = Four().Cast<Player?>().ToList();
        var players = Four();

        // Godtycklig sekvens av byten — mappningen får aldrig få dubbletter.
        current = PlayerSlotMapper.Assign(current, 0, players[3]);
        current = PlayerSlotMapper.Assign(current, 3, players[1]);
        current = PlayerSlotMapper.Assign(current, 1, players[2]);

        Assert.Equal(4, current.Select(p => p!.Id).Distinct().Count());
        var (isValid, error) = MappingValidator.Validate(current);
        Assert.True(isValid, error);
    }

    [Fact]
    public void Assign_TomPosition_FyllsUtanAttTappaNagon()
    {
        var players = Four();
        IReadOnlyList<Player?> current = new Player?[] { players[0], null, players[2], players[3] };

        var next = PlayerSlotMapper.Assign(current, 1, players[1]);

        Assert.Equal("Robin", next[1]!.Name);
        Assert.Equal(4, next.Count(p => p is not null));
    }

    [Fact]
    public void Assign_UtanforIntervall_Kastar()
    {
        var current = Four().Cast<Player?>().ToList();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PlayerSlotMapper.Assign(current, 4, current[0]!));
    }

    [Fact]
    public void Assign_SedanResolve_GerSammaMappningNastaScan()
    {
        // Hela persistens-varvet: byt plats, serialisera, läs tillbaka.
        var players = Four();
        var swapped = PlayerSlotMapper.Assign(players.Cast<Player?>().ToList(), 0, players[1]);

        var json = PlayerPositionMappingCodec.Serialize(swapped.Select(p => p!.Id).ToList());
        var restored = PlayerSlotMapper.Resolve(players, PlayerPositionMappingCodec.TryDeserialize(json));

        Assert.Equal(swapped.Select(p => p!.Name), restored.Select(p => p.Name));
        Assert.Equal("Robin", restored[0].Name);
    }
}
