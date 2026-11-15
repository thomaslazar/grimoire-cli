using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>Pins each league-members request to its route and method.</summary>
public class LeagueMembersServiceTests
{
    private const string Base = "http://example.test/api/leagues/l-1/members";

    private static LeagueMembersService Service() =>
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
    public void AddPostsToMembers() => Assert.Equal(("POST", Base), Of(Service().AddRequest("l-1", "u-1")));

    [Fact]
    public void AddSendsTheUserId() => Assert.Contains("\"user_id\":\"u-1\"", BodyOf(Service().AddRequest("l-1", "u-1")));

    [Fact]
    public void RemoveDeletesTheMember() => Assert.Equal(("DELETE", Base + "/u-1"), Of(Service().RemoveRequest("l-1", "u-1")));

    [Fact]
    public void ArmyPutsToTheMembersArmy() => Assert.Equal(("PUT", Base + "/u-1/army"), Of(Service().ArmyRequest("l-1", "u-1", "a-1")));

    [Fact]
    public void ArmySendsTheArmyId() => Assert.Contains("\"army_id\":\"a-1\"", BodyOf(Service().ArmyRequest("l-1", "u-1", "a-1")));

    [Fact]
    public void ArmyWithoutAnIdSendsNull() => Assert.Contains("\"army_id\":null", BodyOf(Service().ArmyRequest("l-1", "u-1", null)));
}
