using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class CampaignGmPanelsServiceTests
{
    private const string Base = "http://example.test/api/campaigns/c-1/gm-screen/panels";

    private static CampaignGmPanelsService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static (string Method, string Uri) Of(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return (info.HttpMethod.ToString().ToUpperInvariant(), info.URI.AbsoluteUri);
    }

    [Fact]
    public void CreateHitsThePanelsRoute()
        => Assert.Equal(("POST", Base), Of(Service().CreateRequest("c-1", "{\"kind\":\"scratch\"}")));

    [Fact]
    public void UpdateHitsOnePanel()
        => Assert.Equal(("PATCH", Base + "/k-1"), Of(Service().UpdateRequest("c-1", "k-1", "{\"title\":\"T\"}")));

    [Fact]
    public void DeleteHitsOnePanel()
        => Assert.Equal(("DELETE", Base + "/k-1"), Of(Service().DeleteRequest("c-1", "k-1")));

    [Fact]
    public void CreateSendsTheRawBodyUnchanged()
    {
        var info = Service().CreateRequest("c-1", "{\"kind\":\"scratch\",\"state\":{\"text\":\"x\"}}");
        using var reader = new StreamReader(info.Content);
        Assert.Equal("{\"kind\":\"scratch\",\"state\":{\"text\":\"x\"}}", reader.ReadToEnd());
    }
}
