using SQLite;

namespace DoubleDashScore.Models;

[Table("HistoricalRoundPlacements")]
public class HistoricalRoundPlacement
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed, NotNull]
    public int NightNumber { get; set; }

    [Indexed, NotNull]
    public int PlayerId { get; set; }

    [NotNull] public int RoundIndex { get; set; }
    [NotNull] public int Position { get; set; }

    [NotNull] public DateTime CreatedAt { get; set; }
}
