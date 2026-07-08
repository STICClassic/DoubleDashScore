using DoubleDashScore.Models;

namespace DoubleDashScore.Data;

public sealed record RoundDetail(Round Round, IReadOnlyList<RoundResult> Results)
{
    public bool IsComplete => RoundCompletionRule.IsComplete(Round.TrackCount, Results.Count);
}
