namespace DoubleDashScore.Models;

public sealed record PlayerSlotCounters(
    int SlotIndex,
    int FirstPlaces,
    int SecondPlaces,
    int ThirdPlaces,
    int FourthPlaces)
{
    public int Sum => FirstPlaces + SecondPlaces + ThirdPlaces + FourthPlaces;
}

public sealed record ParsedCounters(
    IReadOnlyList<PlayerSlotCounters> Slots,
    int InferredTrackCount,
    IReadOnlyList<string> Warnings);
