using DoubleDashScore.Models;
using DoubleDashScore.Services;

namespace DoubleDashScore.Data;

public class PlayerRepository
{
    private readonly DatabaseService _db;
    private readonly BackupService _backup;

    public PlayerRepository(DatabaseService db, BackupService backup)
    {
        _db = db;
        _backup = backup;
    }

    public async Task<IReadOnlyList<Player>> GetActivePlayersAsync(CancellationToken ct = default)
    {
        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);
        var players = await conn.Table<Player>()
            .Where(p => p.DeletedAt == null)
            .OrderBy(p => p.DisplayOrder)
            .ToListAsync()
            .ConfigureAwait(false);
        return players;
    }

    public async Task UpdateNameAsync(int playerId, string name, CancellationToken ct = default)
    {
        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);
        var player = await conn.FindAsync<Player>(playerId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Player {playerId} not found.");
        player.Name = name;
        await conn.UpdateAsync(player).ConfigureAwait(false);
        _backup.RequestBackup();
    }
}
