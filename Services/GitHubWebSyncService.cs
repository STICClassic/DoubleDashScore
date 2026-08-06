using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace DoubleDashScore.Services;

/// <summary>
/// Resultat av en publicering. <see cref="TokenRejected"/> signalerar att
/// tokenen behöver bytas — vyn visar då inmatningsfältet igen.
/// </summary>
public sealed record SyncResult(bool Success, string Message, bool TokenRejected = false)
{
    public static SyncResult Ok(string message) => new(true, message);

    public static SyncResult Fail(string message, bool tokenRejected = false) =>
        new(false, message, tokenRejected);
}

/// <summary>
/// Pushar appens databas till hemsidans repo via GitHubs Contents API
/// (REST v3). Allt GitHub-specifikt bor här — repo-ägare, repo-namn och
/// sökväg är konstanter, inte spridda strängar i UI-lagret.
/// </summary>
public sealed class GitHubWebSyncService
{
    private const string Owner = "STICClassic";
    private const string Repo = "DoubleDashScore";
    private const string FilePath = "web/data/db.sqlite";
    private const string CommitMessage = "Uppdatera databas från appen";

    // GitHub svarar 403 utan User-Agent. Api-version är den som Contents-
    // endpointen dokumenteras mot.
    private const string UserAgent = "DoubleDashScore-App";
    private const string ApiVersion = "2022-11-28";
    private const string AcceptHeader = "application/vnd.github+json";

    internal const string ContentsUrl =
        $"https://api.github.com/repos/{Owner}/{Repo}/contents/{FilePath}";

    public const string SiteUrl = "https://sticclassic.github.io/DoubleDashScore/";

    private readonly HttpClient _http;
    private readonly IGitHubTokenStore _tokens;
    private readonly string _databasePath;

    public GitHubWebSyncService(HttpClient http, IGitHubTokenStore tokens, string databasePath)
    {
        _http = http;
        _tokens = tokens;
        _databasePath = databasePath;
    }

    public async Task<bool> HasTokenAsync(CancellationToken ct = default)
    {
        var token = await _tokens.GetAsync(ct).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(token);
    }

    public Task SaveTokenAsync(string token, CancellationToken ct = default) =>
        _tokens.SetAsync(token?.Trim(), ct);

    public Task ClearTokenAsync(CancellationToken ct = default) =>
        _tokens.SetAsync(null, ct);

    public async Task<SyncResult> PublishDatabaseAsync(CancellationToken ct = default)
    {
        var token = await _tokens.GetAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            return SyncResult.Fail(
                "Ingen GitHub-token sparad. Ange en token först.", tokenRejected: true);
        }

        if (!File.Exists(_databasePath))
        {
            return SyncResult.Fail("Hittade ingen databasfil att publicera.");
        }

        string base64;
        try
        {
            base64 = Convert.ToBase64String(await ReadDatabaseAsync(ct).ConfigureAwait(false));
        }
        catch (IOException ex)
        {
            return SyncResult.Fail($"Kunde inte läsa databasfilen: {ex.Message}");
        }

        // Contents-API:t kräver befintlig fils SHA vid uppdatering. Saknas filen
        // (404) skickar vi PUT helt utan sha-fält, vilket skapar den i stället.
        var shaLookup = await TryGetExistingShaAsync(token!, ct).ConfigureAwait(false);
        if (shaLookup.Failure is not null) return shaLookup.Failure;

        return await PutFileAsync(token!, base64, shaLookup.Sha, ct).ConfigureAwait(false);
    }

    private async Task<byte[]> ReadDatabaseAsync(CancellationToken ct)
    {
        // FileShare.ReadWrite: SQLite-anslutningen är öppen mot samma fil.
        // Journal mode är DELETE (se CLAUDE.md), så filen är komplett på disk.
        await using var stream = new FileStream(
            _databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private async Task<(string? Sha, SyncResult? Failure)> TryGetExistingShaAsync(
        string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ContentsUrl);
        ApplyHeaders(request, token);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            return (null, SyncResult.Fail(NetworkErrorMessage));
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return (null, null);
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return (null, DescribeFailure((int)response.StatusCode, body));
            }

            var sha = ReadShaFromJson(body);
            if (sha is null)
            {
                return (null, SyncResult.Fail(
                    "GitHub svarade utan sha för filen. Försök igen."));
            }

            return (sha, null);
        }
    }

    private async Task<SyncResult> PutFileAsync(
        string token, string base64, string? sha, CancellationToken ct)
    {
        // Dictionary i stället för anonym typ: sha-fältet ska utelämnas helt när
        // filen inte finns (GitHub avvisar sha: null vid skapande).
        var payload = new Dictionary<string, string>
        {
            ["message"] = CommitMessage,
            ["content"] = base64,
        };
        if (sha is not null) payload["sha"] = sha;

        using var request = new HttpRequestMessage(HttpMethod.Put, ContentsUrl)
        {
            Content = JsonContent.Create(payload),
        };
        ApplyHeaders(request, token);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            return SyncResult.Fail(NetworkErrorMessage);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return SyncResult.Ok("Klart! Hemsidan uppdateras inom 1-2 minuter.");
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return DescribeFailure((int)response.StatusCode, body);
        }
    }

    private static void ApplyHeaders(HttpRequestMessage request, string token)
    {
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("Accept", AcceptHeader);
        request.Headers.Add("User-Agent", UserAgent);
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
    }

    internal const string NetworkErrorMessage =
        "Kunde inte nå GitHub. Kontrollera internetanslutning.";

    private static bool IsNetworkFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException ||
        (ex is TaskCanceledException && !ct.IsCancellationRequested);

    /// <summary>Statuskod → svenskt felmeddelande. Ren funktion, testad separat.</summary>
    internal static SyncResult DescribeFailure(int status, string? responseBody)
    {
        var githubMessage = ReadMessageFromJson(responseBody);

        return status switch
        {
            401 => SyncResult.Fail(
                "Token accepterades inte av GitHub. Byt token nedan.", tokenRejected: true),
            403 => SyncResult.Fail(
                "Åtkomst nekad. Kontrollera att tokenen har Contents: Read and write " +
                "på DoubleDashScore-repot."),
            404 => SyncResult.Fail(
                "Hittade inte filen i repot. Kontrollera att tokenen har åtkomst till " +
                "DoubleDashScore-repot."),
            409 or 422 => SyncResult.Fail(
                "Databasen ändrades på GitHub under tiden. Försök igen."),
            _ => SyncResult.Fail(string.IsNullOrWhiteSpace(githubMessage)
                ? $"GitHub svarade {status}."
                : $"GitHub svarade {status}: {githubMessage}"),
        };
    }

    internal static string? ReadShaFromJson(string? json) => ReadStringProperty(json, "sha");

    private static string? ReadMessageFromJson(string? json) => ReadStringProperty(json, "message");

    private static string? ReadStringProperty(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty(property, out var element)) return null;
            if (element.ValueKind != JsonValueKind.String) return null;
            var value = element.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
