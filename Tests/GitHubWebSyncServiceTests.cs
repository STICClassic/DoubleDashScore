using System.Net;
using System.Text;
using System.Text.Json;
using DoubleDashScore.Services;
using Xunit;

namespace DoubleDashScore.Tests;

public class GitHubWebSyncServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public GitHubWebSyncServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ddsc-websync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "doubledashscore.db3");
        File.WriteAllText(_dbPath, "hej");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private GitHubWebSyncService Build(FakeHandler handler, string? token = "tok123", string? dbPath = null)
        => new(new HttpClient(handler), new FakeTokenStore(token), dbPath ?? _dbPath);

    [Fact]
    public async Task PublishAsync_UtanToken_GerFelOchBerOmNyToken()
    {
        var handler = new FakeHandler();
        var service = Build(handler, token: null);

        var result = await service.PublishDatabaseAsync();

        Assert.False(result.Success);
        Assert.True(result.TokenRejected);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PublishAsync_UtanDatabasfil_GerFel()
    {
        var handler = new FakeHandler();
        var service = Build(handler, dbPath: Path.Combine(_tempDir, "finns-inte.db3"));

        var result = await service.PublishDatabaseAsync();

        Assert.False(result.Success);
        Assert.Contains("databasfil", result.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PublishAsync_BefintligFil_SkickarShaOchBase64()
    {
        var handler = new FakeHandler(
            Json(HttpStatusCode.OK, """{"sha":"abc123"}"""),
            Json(HttpStatusCode.OK, """{"commit":{}}"""));
        var service = Build(handler);

        var result = await service.PublishDatabaseAsync();

        Assert.True(result.Success);
        Assert.Contains("1-2 minuter", result.Message);

        var put = handler.Requests[1];
        Assert.Equal(HttpMethod.Put, put.Method);
        using var body = JsonDocument.Parse(put.Body!);
        Assert.Equal("Uppdatera databas från appen", body.RootElement.GetProperty("message").GetString());
        Assert.Equal("abc123", body.RootElement.GetProperty("sha").GetString());
        Assert.Equal(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("hej")),
            body.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task PublishAsync_SkickarObligatoriskaHeaders()
    {
        var handler = new FakeHandler(
            Json(HttpStatusCode.OK, """{"sha":"abc123"}"""),
            Json(HttpStatusCode.OK, "{}"));
        var service = Build(handler);

        await service.PublishDatabaseAsync();

        foreach (var request in handler.Requests)
        {
            Assert.Equal("Bearer tok123", request.Headers["Authorization"]);
            Assert.Equal("application/vnd.github+json", request.Headers["Accept"]);
            Assert.Equal("DoubleDashScore-App", request.Headers["User-Agent"]);
            Assert.Equal("2022-11-28", request.Headers["X-GitHub-Api-Version"]);
            Assert.Equal(GitHubWebSyncService.ContentsUrl, request.Url);
        }
    }

    [Fact]
    public async Task PublishAsync_NarFilenSaknas_SkickarPutUtanSha()
    {
        var handler = new FakeHandler(
            Json(HttpStatusCode.NotFound, """{"message":"Not Found"}"""),
            Json(HttpStatusCode.Created, "{}"));
        var service = Build(handler);

        var result = await service.PublishDatabaseAsync();

        Assert.True(result.Success);
        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.False(body.RootElement.TryGetProperty("sha", out _));
    }

    [Fact]
    public async Task PublishAsync_401_BerAnvandarenByteToken()
    {
        var handler = new FakeHandler(Json(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}"""));
        var service = Build(handler);

        var result = await service.PublishDatabaseAsync();

        Assert.False(result.Success);
        Assert.True(result.TokenRejected);
        Assert.Contains("Byt token", result.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PublishAsync_403_NamnerContentsBehorighet()
    {
        var handler = new FakeHandler(
            Json(HttpStatusCode.OK, """{"sha":"abc123"}"""),
            Json(HttpStatusCode.Forbidden, """{"message":"Resource not accessible"}"""));
        var service = Build(handler);

        var result = await service.PublishDatabaseAsync();

        Assert.False(result.Success);
        Assert.False(result.TokenRejected);
        Assert.Contains("Contents: Read and write", result.Message);
    }

    [Fact]
    public async Task PublishAsync_Natverksfel_GerOfflinemeddelande()
    {
        var handler = new FakeHandler { Throw = new HttpRequestException("no network") };
        var service = Build(handler);

        var result = await service.PublishDatabaseAsync();

        Assert.False(result.Success);
        Assert.Equal(GitHubWebSyncService.NetworkErrorMessage, result.Message);
    }

    [Fact]
    public async Task PublishAsync_Timeout_GerOfflinemeddelande()
    {
        var handler = new FakeHandler { Throw = new TaskCanceledException("timeout") };
        var service = Build(handler);

        var result = await service.PublishDatabaseAsync();

        Assert.False(result.Success);
        Assert.Equal(GitHubWebSyncService.NetworkErrorMessage, result.Message);
    }

    [Fact]
    public async Task HasTokenAsync_ReflekterarLagretsInnehall()
    {
        Assert.True(await Build(new FakeHandler()).HasTokenAsync());
        Assert.False(await Build(new FakeHandler(), token: null).HasTokenAsync());
        Assert.False(await Build(new FakeHandler(), token: "   ").HasTokenAsync());
    }

    [Fact]
    public async Task SaveTokenAsync_TrimmarInnanLagring()
    {
        var store = new FakeTokenStore(null);
        var service = new GitHubWebSyncService(new HttpClient(new FakeHandler()), store, _dbPath);

        await service.SaveTokenAsync("  ghp_abc  ");

        Assert.Equal("ghp_abc", await store.GetAsync());
    }

    [Fact]
    public async Task ClearTokenAsync_TommerLagret()
    {
        var store = new FakeTokenStore("ghp_abc");
        var service = new GitHubWebSyncService(new HttpClient(new FakeHandler()), store, _dbPath);

        await service.ClearTokenAsync();

        Assert.Null(await store.GetAsync());
    }

    [Fact]
    public void DescribeFailure_OkandStatus_VisarKodOchGitHubsMeddelande()
    {
        var result = GitHubWebSyncService.DescribeFailure(500, """{"message":"Server Error"}""");

        Assert.False(result.Success);
        Assert.Contains("500", result.Message);
        Assert.Contains("Server Error", result.Message);
    }

    [Fact]
    public void DescribeFailure_UtanBody_VisarBaraKoden()
    {
        var result = GitHubWebSyncService.DescribeFailure(502, null);

        Assert.Equal("GitHub svarade 502.", result.Message);
    }

    [Theory]
    [InlineData(409)]
    [InlineData(422)]
    public void DescribeFailure_Konflikt_BerAnvandarenForsokaIgen(int status)
    {
        var result = GitHubWebSyncService.DescribeFailure(status, null);

        Assert.Contains("Försök igen", result.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("inte json")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"sha":123}""")]
    public void ReadShaFromJson_TalerSkrapinput(string? json)
    {
        Assert.Null(GitHubWebSyncService.ReadShaFromJson(json));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeTokenStore : IGitHubTokenStore
    {
        private string? _token;

        public FakeTokenStore(string? token) => _token = token;

        public Task<string?> GetAsync(CancellationToken ct = default) => Task.FromResult(_token);

        public Task SetAsync(string? token, CancellationToken ct = default)
        {
            _token = string.IsNullOrWhiteSpace(token) ? null : token;
            return Task.CompletedTask;
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method, string Url, Dictionary<string, string> Headers, string? Body);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public FakeHandler(params HttpResponseMessage[] responses) =>
            _responses = new Queue<HttpResponseMessage>(responses);

        public Exception? Throw { get; init; }

        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
                body));

            if (Throw is not null) throw Throw;

            return _responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
        }
    }
}
