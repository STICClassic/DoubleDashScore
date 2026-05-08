using DoubleDashScore.Models;

namespace DoubleDashScore.Data;

public sealed record RoundDetail(Round Round, IReadOnlyList<RoundResult> Results)
{
    public bool IsComplete => Round.TrackCount == 16 && Results.Count == 4;
}
