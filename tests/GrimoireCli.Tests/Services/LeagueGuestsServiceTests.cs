using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>Pins each league-guests request to its route and method.</summary>
public class LeagueGuestsServiceTests
{
    private const string Base = "http://example.test/api/leagues/l-1/guests";

    private static LeagueGuestsService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static (string Method, string Uri) Of(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return (info.HttpMethod.ToString().ToUpperInvariant(), info.URI.AbsoluteUri);
    }

    private static string BodyOf(RequestInformation info)
    {
        using var reader = new StreamReader(info.Content);
        return reader.ReadToEnd().Replace(" ", "");
    }

    [Fact]
    public void ListGetsGuests() => Assert.Equal(("GET", Base), Of(Service().ListRequest("l-1")));

    [Fact]
    public void CreatePostsToGuests() => Assert.Equal(("POST", Base), Of(Service().CreateRequest("l-1", null)));

    [Fact]
    public void RegeneratePostsToTheGuest() => Assert.Equal(("POST", Base + "/u-1/regenerate"), Of(Service().RegenerateRequest("l-1", "u-1")));

    [Fact]
    public void ShareGetsTheShare() => Assert.Equal(("GET", Base + "/u-1/share"), Of(Service().ShareRequest("l-1", "u-1")));

    [Fact]
    public void RemoveDeletesTheGuest() => Assert.Equal(("DELETE", Base + "/u-1"), Of(Service().RemoveRequest("l-1", "u-1")));

    [Fact]
    public void CreateSendsTheNicknameOnlyWhenGiven()
    {
        Assert.Contains("\"nickname\":\"Sam\"", BodyOf(Service().CreateRequest("l-1", "Sam")));
        Assert.DoesNotContain("nickname", BodyOf(Service().CreateRequest("l-1", null)));
    }
}
