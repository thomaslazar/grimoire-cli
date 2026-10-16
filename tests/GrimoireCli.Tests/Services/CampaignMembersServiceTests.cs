using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class CampaignMembersServiceTests
{
    private const string Base = "http://example.test/api/campaigns";

    private static GrimoireApiClient Client() => new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static (string Method, string Uri) Of(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return (info.HttpMethod.ToString().ToUpperInvariant(), info.URI.AbsoluteUri);
    }

    private static string Body(RequestInformation info)
    {
        using var reader = new StreamReader(info.Content);
        return reader.ReadToEnd();
    }

    public static TheoryData<string, string, string> Routes() => new()
    {
        { "eligible", "GET", Base + "/c-1/eligible-members" },
        { "invite", "POST", Base + "/c-1/invite" },
        { "update", "PATCH", Base + "/c-1/members/u-1" },
        { "remove", "DELETE", Base + "/c-1/members/u-1" },
        { "invites", "GET", Base + "/invites" },
    };

    private static RequestInformation Request(string verb)
    {
        var m = new CampaignMembersService(Client());
        return verb switch
        {
            "eligible" => m.EligibleRequest("c-1"),
            "invite" => m.InviteRequest("c-1", "u-1"),
            "update" => m.UpdateRequest("c-1", "u-1", "accepted", null, null),
            "remove" => m.RemoveRequest("c-1", "u-1"),
            "invites" => new CampaignsService(Client()).InvitesRequest(),
            _ => throw new ArgumentException($"Unknown verb '{verb}'.", nameof(verb)),
        };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void EachRequestHitsItsRouteWithItsMethod(string verb, string method, string uri)
        => Assert.Equal((method, uri), Of(Request(verb)));

    [Fact]
    public void InviteSendsTheUserId()
        => Assert.Contains("\"user_id\":\"u-1\"", Body(new CampaignMembersService(Client()).InviteRequest("c-1", "u-1")));

    [Fact]
    public void UpdateWithOnlyAStatusSendsOnlyTheStatus()
        => Assert.Equal("{\"status\":\"accepted\"}", Body(new CampaignMembersService(Client()).UpdateRequest("c-1", "u-1", "accepted", null, null)));

    [Fact]
    public void UpdateSendsAnEmptyStringToClear()
        => Assert.Contains("\"character_name\":\"\"", Body(new CampaignMembersService(Client()).UpdateRequest("c-1", "u-1", null, "", null)));
}
