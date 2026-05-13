namespace DoubleDashScore.Services;

public sealed class SecureStorageApiKeyStore : IApiKeyStore
{
    private const string Key = "anthropic_api_key";

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

    public Task SetAsync(string? key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            SecureStorage.Default.Remove(Key);
            return Task.CompletedTask;
        }
        return SecureStorage.Default.SetAsync(Key, key);
    }
}
