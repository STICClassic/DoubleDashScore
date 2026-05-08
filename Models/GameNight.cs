using SQLite;

namespace DoubleDashScore.Models;

[Table("GameNights")]
public class GameNight
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull]
    public DateTime PlayedOn { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    [NotNull]
    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
