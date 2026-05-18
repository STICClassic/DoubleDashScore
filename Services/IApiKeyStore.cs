namespace DoubleDashScore.Services;

public interface IApiKeyStore
{
    Task<string?> GetAsync(CancellationToken ct = default);
    Task SetAsync(string? key, CancellationToken ct = default);
}
