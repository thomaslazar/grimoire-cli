using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class CampaignScheduleServiceTests
{
    private const string Base = "http://example.test/api/campaigns/c-1";

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
        { "get", "GET", Base + "/schedule" },
        { "set", "PUT", Base + "/schedule" },
        { "delete", "DELETE", Base + "/schedule" },
    };

    private static RequestInformation Request(string verb)
    {
        var s = new CampaignScheduleService(Client());
        return verb switch
        {
            "get" => s.GetRequest("c-1"),
            "set" => s.SetRequest("c-1", "{}"),
            "delete" => s.DeleteRequest("c-1"),
            _ => throw new ArgumentException($"Unknown verb '{verb}'.", nameof(verb)),
        };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void EachRequestHitsItsRouteWithItsMethod(string verb, string method, string uri)
        => Assert.Equal((method, uri), Of(Request(verb)));

    [Fact]
    public void SetSendsTheRawBodyUnchanged()
        => Assert.Equal("{\"frequency\":\"weekly\"}", Body(new CampaignScheduleService(Client()).SetRequest("c-1", "{\"frequency\":\"weekly\"}")));
}
