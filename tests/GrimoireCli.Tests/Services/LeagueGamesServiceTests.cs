using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>Pins each league-games request to its route and method.</summary>
public class LeagueGamesServiceTests
{
    private const string Base = "http://example.test/api/leagues/l-1/games";

    private static LeagueGamesService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static (string Method, string Uri) Of(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return (info.HttpMethod.ToString().ToUpperInvariant(), info.URI.AbsoluteUri);
    }

    [Fact]
    public void CreatePostsToGames() => Assert.Equal(("POST", Base), Of(Service().CreateRequest("l-1", "{}")));

    [Fact]
    public void UpdatePutsTheGame() => Assert.Equal(("PUT", Base + "/g-1"), Of(Service().UpdateRequest("l-1", "g-1", "{}")));

    [Fact]
    public void DeleteDeletesTheGame() => Assert.Equal(("DELETE", Base + "/g-1"), Of(Service().DeleteRequest("l-1", "g-1")));

    [Fact]
    public void CreateSendsTheRawBody()
    {
        var info = Service().CreateRequest("l-1", "{\"score_a\":1}");
        using var reader = new StreamReader(info.Content);
        Assert.Equal("{\"score_a\":1}", reader.ReadToEnd());
    }
}
