namespace DoubleDashScore.Services;

public sealed class SecureStorageGitHubTokenStore : IGitHubTokenStore
{
    private const string Key = "github_pat";

    public async Task<string?> GetAsync(CancellationToken ct = default)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(Key).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public Task SetAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            SecureStorage.Default.Remove(Key);
            return Task.CompletedTask;
        }
        return SecureStorage.Default.SetAsync(Key, token);
    }
}
