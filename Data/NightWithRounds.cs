using DoubleDashScore.Models;

namespace DoubleDashScore.Data;

public sealed record NightWithRounds(GameNight Night, IReadOnlyList<RoundDetail> Rounds);
