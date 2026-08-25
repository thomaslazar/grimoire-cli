using System.Net;
using System.Text;
using GrimoireCli.Api;
using GrimoireCli.Configuration;

namespace GrimoireCli.Tests.Api;

public class TokenRefreshTests
{
    // Signature-free JWTs: only the exp claim is read, by TokenHelper.
    private static string Jwt(int secondsFromNow)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var exp = DateTimeOffset.UtcNow.AddSeconds(secondsFromNow).ToUnixTimeSeconds();
        return $"{B64("{\"alg\":\"HS256\"}")}.{B64($"{{\"exp\":{exp}}}")}.sig";
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(string Path, string Body, string? Auth, string? Cookie)> Seen { get; } = new();
        public Func<int, HttpResponseMessage>? Respond { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null
                ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            request.Headers.TryGetValues("Cookie", out var cookie);
            Seen.Add((request.RequestUri!.AbsolutePath, body,
                request.Headers.Authorization?.Parameter, cookie?.FirstOrDefault()));
            return Respond!(Seen.Count - 1);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage ExpiredTokenUnauthorized()
    {
        var response = Json(HttpStatusCode.Unauthorized,
            "{\"detail\":\"Token expired - please log in again\"}");
        response.Headers.Add("X-Token-Expired", "1");
        return response;
    }

    private static HttpResponseMessage RefreshOk(string newAccess, string newRefresh)
    {
        var response = Json(HttpStatusCode.OK, $"{{\"token\":\"{newAccess}\"}}");
        response.Headers.TryAddWithoutValidation("Set-Cookie",
            $"grimoire_refresh={newRefresh}; HttpOnly; Path=/api/auth; SameSite=strict");
        return response;
    }

    private static (GrimoireApiClient client, ConfigManager manager, string path, AppConfig config)
        Build(RecordingHandler handler, string accessToken, string? refreshToken)
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        var manager = new ConfigManager(path);
        var config = new AppConfig
        {
            Server = "http://grimoire.test",
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            // Keeps PreflightAsync from probing /api/about through the stub.
            LastVersionCheck = DateTimeOffset.UtcNow,
            LastServerVersion = "nightly"
        };
        manager.Save(config);
        return (new GrimoireApiClient(config, manager, handler), manager, path, config);
    }

    [Theory]
    [InlineData(30, true, true)]    // inside the 60s threshold, cookie held
    [InlineData(30, false, false)]  // inside it, but nothing to refresh with
    [InlineData(3600, true, false)] // plenty of life left
    public void ShouldRefreshProactively_OnlyInsideTheThresholdAndWithACookie(
        int secondsLeft, bool haveRefreshToken, bool expected)
        => Assert.Equal(expected,
            GrimoireApiClient.ShouldRefreshProactively(Jwt(secondsLeft), haveRefreshToken));

    [Fact]
    public void ShouldRefreshProactively_IsFalseWithNoAccessToken()
        => Assert.False(GrimoireApiClient.ShouldRefreshProactively(null, haveRefreshToken: true));

    [Fact]
    public void ShouldRefreshOn401_TrueForAnExpiredTokenWithACookie()
        => Assert.True(GrimoireApiClient.ShouldRefreshOn401(
            ExpiredTokenUnauthorized(), haveRefreshToken: true));

    // A bare 401 is "not authenticated" or "invalid token" — refreshing would
    // spend a request against a rate-limited endpoint for nothing.
    [Fact]
    public void ShouldRefreshOn401_FalseWithoutTheHeader()
        => Assert.False(GrimoireApiClient.ShouldRefreshOn401(
            Json(HttpStatusCode.Unauthorized, "{\"detail\":\"Not authenticated\"}"),
            haveRefreshToken: true));

    [Fact]
    public void ShouldRefreshOn401_FalseForAPermissionDenial()
        => Assert.False(GrimoireApiClient.ShouldRefreshOn401(
            Json(HttpStatusCode.Forbidden, "{\"detail\":\"Forbidden\"}"), haveRefreshToken: true));

    [Fact]
    public void ShouldRefreshOn401_FalseWithNoCookieHeld()
        => Assert.False(GrimoireApiClient.ShouldRefreshOn401(
            ExpiredTokenUnauthorized(), haveRefreshToken: false));

    // The whole point of the reactive path: a PATCH that meets an expired token
    // must reach the server a second time with its body intact. abs-cli's
    // equivalent rebuilds the request without content and silently sends nothing.
    [Fact]
    public async Task RetryAfterRefresh_ResendsTheOriginalBody()
    {
        // A different lifetime from the token Build starts with: two JWTs minted a
        // moment apart for the same lifetime are byte-identical, which would make
        // "the retry carries the new token" unfalsifiable.
        var refreshed = Jwt(3600);
        var handler = new RecordingHandler
        {
            Respond = i => i switch
            {
                0 => ExpiredTokenUnauthorized(),
                1 => RefreshOk(refreshed, "rotated-cookie"),
                _ => Json(HttpStatusCode.OK, "{\"id\":\"sys-1\"}")
            }
        };
        var (client, manager, path, _) = Build(handler, Jwt(1800), "stored-cookie");
        try
        {
            // Mirrors SystemsService.UpdateAsync: an empty generated model carries
            // the shape, then the validated raw body replaces the content. That is
            // the only way a PATCH body is built in this CLI, so it is the shape
            // the retry has to preserve.
            var info = client.Api.Api.Systems["sys-1"].ToPatchRequestInformation(
                new GrimoireCli.Generated.Models.GameSystemUpdate());
            info.SetStreamContent(
                new MemoryStream(Encoding.UTF8.GetBytes("{\"name\":\"Renamed\"}")),
                "application/json");

            var body = await client.SendAsync(info);

            Assert.Equal("{\"id\":\"sys-1\"}", body);
            Assert.Equal(3, handler.Seen.Count);
            Assert.Equal("/api/auth/refresh", handler.Seen[1].Path);
            Assert.Equal("grimoire_refresh=stored-cookie", handler.Seen[1].Cookie);
            // The retry carries the same body as the first attempt.
            Assert.Contains("Renamed", handler.Seen[0].Body);
            Assert.Equal(handler.Seen[0].Body, handler.Seen[2].Body);
            // ...and the new access token, not the dead one.
            Assert.Equal(refreshed, handler.Seen[2].Auth);
            Assert.NotEqual(handler.Seen[0].Auth, handler.Seen[2].Auth);
            // The rotated pair is on disk for the next invocation.
            var saved = manager.Load();
            Assert.Equal("rotated-cookie", saved.RefreshToken);
            Assert.Equal(handler.Seen[2].Auth, saved.AccessToken);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task ProactiveRefresh_HappensBeforeTheRequest()
    {
        var handler = new RecordingHandler
        {
            Respond = i => i == 0
                ? RefreshOk(Jwt(1800), "rotated-cookie")
                : Json(HttpStatusCode.OK, "[]")
        };
        var (client, _, path, _) = Build(handler, Jwt(10), "stored-cookie");
        try
        {
            await client.SendAsync(client.Api.Api.Systems.ToGetRequestInformation());
            Assert.Equal(2, handler.Seen.Count);
            Assert.Equal("/api/auth/refresh", handler.Seen[0].Path);
            Assert.Equal("/api/systems", handler.Seen[1].Path);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task NoRefreshTokenMeansNoRefreshAttempt()
    {
        var handler = new RecordingHandler { Respond = _ => Json(HttpStatusCode.OK, "[]") };
        var (client, _, path, _) = Build(handler, Jwt(10), refreshToken: null);
        try
        {
            await client.SendAsync(client.Api.Api.Systems.ToGetRequestInformation());
            Assert.Single(handler.Seen);
            Assert.Equal("/api/systems", handler.Seen[0].Path);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
