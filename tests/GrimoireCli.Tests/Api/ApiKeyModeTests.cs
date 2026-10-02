using System.Net;
using System.Text;
using GrimoireCli.Api;
using GrimoireCli.Configuration;

namespace GrimoireCli.Tests.Api;

// Sends through GrimoireApiClient's pipeline, so it emits DebugHttpHandler log
// lines into the global NLog target, as TokenRefreshTests does.
[Collection("NLog")]
public class ApiKeyModeTests
{
    private const string Url = GrimoireApiClient.KeyPermissionsUrl;
    private const string KeyPointer = "\nKey permissions per command: " + Url;

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Seen { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        }
    }

    private static string Jwt(int secondsFromNow)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var exp = DateTimeOffset.UtcNow.AddSeconds(secondsFromNow).ToUnixTimeSeconds();
        return $"{B64("{\"alg\":\"HS256\"}")}.{B64($"{{\"exp\":{exp}}}")}.sig";
    }

    private static async Task<(RecordingHandler handler, GrimoireApiClient client)> SendOne(AppConfig config)
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var manager = new ConfigManager(Path.Combine(dir, "config.json"));
        config.Server = "http://grimoire.test";
        // Keeps PreflightAsync from probing /api/about through the stub.
        config.LastVersionCheck = DateTimeOffset.UtcNow;
        config.LastServerVersion = "nightly";
        manager.Save(config);
        var handler = new RecordingHandler();
        var client = new GrimoireApiClient(config, manager, handler);
        try
        {
            await client.SendAsync(client.Api.Api.Systems.ToGetRequestInformation());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
        return (handler, client);
    }

    [Fact]
    public void ErrorMessage_SessionUnauthorizedPointsAtPasswordLogin()
    {
        var message = GrimoireApiClient.ErrorMessage(401, "Unauthorized", "", null, null, apiKeyMode: false);
        Assert.Contains("grimoire-cli login", message);
        Assert.DoesNotContain("api-key", message);
    }

    [Fact]
    public void ErrorMessage_KeyUnauthorizedPointsAtKeyLoginAndDocs()
        => Assert.Equal(
            "API key invalid or expired. Run: grimoire-cli login --api-key-stdin" + KeyPointer,
            GrimoireApiClient.ErrorMessage(401, "Unauthorized", "{\"detail\":\"x\"}", null, null, apiKeyMode: true));

    [Fact]
    public void ErrorMessage_ForbiddenWithHintCarriesTheBody()
        => Assert.Equal(
            "Permission denied. This operation requires the admin role. {\"detail\":\"x\"}",
            GrimoireApiClient.ErrorMessage(403, "Forbidden", "{\"detail\":\"x\"}", "the admin role", null, apiKeyMode: false));

    [Fact]
    public void ErrorMessage_ForbiddenWithHintAndNoBodyHasNoTrailingSpace()
        => Assert.Equal(
            "Permission denied. This operation requires the admin role.",
            GrimoireApiClient.ErrorMessage(403, "Forbidden", "", "the admin role", null, apiKeyMode: false));

    [Fact]
    public void ErrorMessage_KeyForbiddenEndsWithTheDocsLine()
        => Assert.Equal(
            "Permission denied. This operation requires the admin role. {\"detail\":\"x\"}" + KeyPointer,
            GrimoireApiClient.ErrorMessage(403, "Forbidden", "{\"detail\":\"x\"}", "the admin role", null, apiKeyMode: true));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ErrorMessage_TooManyRequestsCarriesTheBody(bool apiKeyMode)
        => Assert.Equal(
            "Too many requests. {\"detail\":\"slow\"}",
            GrimoireApiClient.ErrorMessage(429, "Too Many Requests", "{\"detail\":\"slow\"}", null, null, apiKeyMode));

    [Fact]
    public void ErrorMessage_NotFoundHintUnchanged()
        => Assert.Equal(
            "Not found. Check the id.",
            GrimoireApiClient.ErrorMessage(404, "Not Found", "{}", null, "Check the id.", apiKeyMode: true));

    [Fact]
    public async Task KeySendsXApiKeyAndNoBearer()
    {
        var (handler, _) = await SendOne(new AppConfig { ApiKey = "grim_x", AccessToken = "stale" });
        var request = Assert.Single(handler.Seen);
        Assert.Equal("grim_x", request.Headers.GetValues(GrimoireApiClient.ApiKeyHeader).Single());
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task SessionSendsBearerAndNoXApiKey()
    {
        var (handler, _) = await SendOne(new AppConfig { AccessToken = Jwt(3600) });
        var request = Assert.Single(handler.Seen);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.False(request.Headers.Contains(GrimoireApiClient.ApiKeyHeader));
    }

    // A nearly expired stale token plus a stored cookie would renew in session
    // mode; under a key nothing may reach /api/auth/refresh.
    [Fact]
    public async Task KeyNeverRefreshesProactively()
    {
        var (handler, _) = await SendOne(new AppConfig
        {
            ApiKey = "grim_x",
            AccessToken = Jwt(10),
            RefreshToken = "cookie"
        });
        var request = Assert.Single(handler.Seen);
        Assert.Equal("/api/systems", request.RequestUri!.AbsolutePath);
    }

    // The reactive path ends in Environment.Exit on the 401, so the gate it
    // reads is asserted directly.
    [Fact]
    public async Task KeyNeverRefreshesOn401()
    {
        var (_, client) = await SendOne(new AppConfig { ApiKey = "grim_x", RefreshToken = "cookie" });
        Assert.False(client.CanRefresh);
    }
}
