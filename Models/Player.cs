using SQLite;

namespace DoubleDashScore.Models;

[Table("Players")]
public class Player
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [MaxLength(50), NotNull]
    public string Name { get; set; } = string.Empty;

    [NotNull]
    public int DisplayOrder { get; set; }

    [NotNull]
    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
