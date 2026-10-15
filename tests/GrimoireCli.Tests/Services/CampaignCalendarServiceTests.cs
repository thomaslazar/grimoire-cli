using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class CampaignCalendarServiceTests
{
    private const string Base = "http://example.test/api/campaigns";

    private static GrimoireApiClient Client() => new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static (string Method, string Uri) Of(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return (info.HttpMethod.ToString().ToUpperInvariant(), info.URI.AbsoluteUri);
    }

    [Fact]
    public void DownloadHitsTheCampaignIcs()
        => Assert.Equal(("GET", Base + "/c-1/calendar.ics"), Of(new CampaignCalendarService(Client()).DownloadRequest("c-1")));

    [Fact]
    public void SubscriptionRoutesUseTheirMethods()
    {
        var s = new CampaignCalendarService(Client());
        Assert.Equal(("GET", Base + "/calendar/subscription"), Of(s.SubscriptionRequest(null)));
        Assert.Equal(("POST", Base + "/calendar/subscription"), Of(s.CreateSubscriptionRequest(null)));
        Assert.Equal(("DELETE", Base + "/calendar/subscription"), Of(s.RevokeSubscriptionRequest()));
    }

    [Fact]
    public void CampaignIdIsSentOnlyWhenGiven()
    {
        var s = new CampaignCalendarService(Client());
        Assert.Equal(("GET", Base + "/calendar/subscription?campaign_id=c-1"), Of(s.SubscriptionRequest("c-1")));
        Assert.Equal(("POST", Base + "/calendar/subscription?campaign_id=c-1"), Of(s.CreateSubscriptionRequest("c-1")));
    }
}
