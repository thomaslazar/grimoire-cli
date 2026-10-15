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
        { "schedule get", "GET", Base + "/schedule" },
        { "schedule set", "PUT", Base + "/schedule" },
        { "schedule delete", "DELETE", Base + "/schedule" },
        { "availability get", "GET", Base + "/availability" },
        { "availability set", "PUT", Base + "/availability/2026-10-21" },
        { "toggle", "PUT", Base + "/availability/2026-10-21/cancel" },
    };

    private static RequestInformation Request(string verb)
    {
        var s = new CampaignScheduleService(Client());
        var a = new CampaignAvailabilityService(Client());
        return verb switch
        {
            "schedule get" => s.GetRequest("c-1"),
            "schedule set" => s.SetRequest("c-1", "{}"),
            "schedule delete" => s.DeleteRequest("c-1"),
            "availability get" => a.GetRequest("c-1"),
            "availability set" => a.SetRequest("c-1", "2026-10-21", "available", null, null),
            "toggle" => a.ToggleCancelRequest("c-1", "2026-10-21"),
            _ => throw new ArgumentException($"Unknown verb '{verb}'.", nameof(verb)),
        };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void EachRequestHitsItsRouteWithItsMethod(string verb, string method, string uri)
        => Assert.Equal((method, uri), Of(Request(verb)));

    [Fact]
    public void ScheduleSetSendsTheRawBodyUnchanged()
        => Assert.Equal("{\"frequency\":\"weekly\"}", Body(new CampaignScheduleService(Client()).SetRequest("c-1", "{\"frequency\":\"weekly\"}")));

    [Fact]
    public void AvailabilitySetWithOnlyAStatusSendsOnlyTheStatus()
        => Assert.Equal("{\"status\":\"tentative\"}", Body(new CampaignAvailabilityService(Client()).SetRequest("c-1", "2026-10-21", "tentative", null, null)));

    [Fact]
    public void AvailabilitySetSendsCancelledAndUserIdWhenGiven()
    {
        var body = Body(new CampaignAvailabilityService(Client()).SetRequest("c-1", "2026-10-21", "available", true, "u-1"));
        Assert.Contains("\"is_cancelled\":true", body);
        Assert.Contains("\"user_id\":\"u-1\"", body);
    }
}
