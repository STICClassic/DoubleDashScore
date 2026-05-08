using SQLite;

namespace DoubleDashScore.Models;

[Table("Rounds")]
public class Round
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed, NotNull]
    public int GameNightId { get; set; }

    [NotNull]
    public int RoundNumber { get; set; }

    [NotNull]
    public int TrackCount { get; set; }

    [NotNull]
    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
