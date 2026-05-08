using SQLite;

namespace DoubleDashScore.Models;

[Table("RoundResults")]
public class RoundResult
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed, NotNull]
    public int RoundId { get; set; }

    [Indexed, NotNull]
    public int PlayerId { get; set; }

    [NotNull]
    public int FirstPlaces { get; set; }

    [NotNull]
    public int SecondPlaces { get; set; }

    [NotNull]
    public int ThirdPlaces { get; set; }

    [NotNull]
    public int FourthPlaces { get; set; }

    [NotNull]
    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
