using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class CampaignGuestsServiceTests
{
    private const string Base = "http://example.test/api/campaigns/c-1/guests";

    private static CampaignGuestsService Service() => new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

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
        { "list", "GET", Base },
        { "create", "POST", Base },
        { "regenerate", "POST", Base + "/m-1/regenerate" },
        { "remove", "DELETE", Base + "/m-1" },
        { "share", "GET", Base + "/m-1/share-template" },
    };

    private static RequestInformation Request(string verb)
    {
        var s = Service();
        return verb switch
        {
            "list" => s.ListRequest("c-1"),
            "create" => s.CreateRequest("c-1", null),
            "regenerate" => s.RegenerateRequest("c-1", "m-1"),
            "remove" => s.RemoveRequest("c-1", "m-1"),
            "share" => s.ShareRequest("c-1", "m-1"),
            _ => throw new ArgumentException($"Unknown verb '{verb}'.", nameof(verb)),
        };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void EachRequestHitsItsRouteWithItsMethod(string verb, string method, string uri)
        => Assert.Equal((method, uri), Of(Request(verb)));

    [Fact]
    public void CreateWithoutANicknameSendsNoNicknameKey()
        => Assert.DoesNotContain("nickname", Body(Service().CreateRequest("c-1", null)));

    [Fact]
    public void CreateSendsTheNickname()
        => Assert.Contains("\"nickname\":\"Sam\"", Body(Service().CreateRequest("c-1", "Sam")));
}
