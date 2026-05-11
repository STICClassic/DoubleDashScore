using SQLite;

namespace DoubleDashScore.Models;

[Table("HistoricalPositionTotalsSnapshot")]
public class HistoricalPositionTotalsSnapshot
{
    [PrimaryKey]
    public int PlayerId { get; set; }

    [NotNull] public int Firsts { get; set; }
    [NotNull] public int Seconds { get; set; }
    [NotNull] public int Thirds { get; set; }
    [NotNull] public int Fourths { get; set; }

    [NotNull] public DateTime CreatedAt { get; set; }
}
