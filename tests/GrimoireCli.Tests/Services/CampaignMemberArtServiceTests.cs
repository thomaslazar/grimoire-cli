using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class CampaignMemberArtServiceTests
{
    private const string Base = "http://example.test/api/campaigns/c-1/members/m-1/art";

    private static CampaignMemberArtService Service() => new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static (string Method, string Uri) Of(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return (info.HttpMethod.ToString().ToUpperInvariant(), info.URI.AbsoluteUri);
    }

    [Fact]
    public void GetHitsItsRoute() => Assert.Equal(("GET", Base), Of(Service().GetRequest("c-1", "m-1")));

    [Fact]
    public void UploadHitsItsRoute() => Assert.Equal(("POST", Base), Of(Service().UploadRequest("c-1", "m-1", [1], "a.png")));

    [Fact]
    public void DeleteHitsItsRoute() => Assert.Equal(("DELETE", Base), Of(Service().DeleteRequest("c-1", "m-1")));

    [Fact]
    public void UploadSendsAnImagePartNamedFile()
    {
        using var reader = new StreamReader(Service().UploadRequest("c-1", "m-1", [1], "a.png").Content);
        var body = reader.ReadToEnd();
        Assert.Contains("name=\"file\"", body);
        Assert.Contains("Content-Type: image/png", body);
    }
}
