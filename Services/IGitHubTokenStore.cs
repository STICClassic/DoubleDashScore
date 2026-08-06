namespace DoubleDashScore.Services;

/// <summary>
/// Lagring av GitHub Personal Access Token. Samma mönster som
/// <see cref="IApiKeyStore"/> — interfacet håller GitHubWebSyncService fri
/// från MAUI-beroenden så den kan enhetstestas.
/// </summary>
public interface IGitHubTokenStore
{
    Task<string?> GetAsync(CancellationToken ct = default);
    Task SetAsync(string? token, CancellationToken ct = default);
}
