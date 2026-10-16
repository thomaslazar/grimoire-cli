using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class CampaignMemberSheetServiceTests
{
    private const string Base = "http://example.test/api/campaigns/c-1";

    private static CampaignMemberSheetService Service() => new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static (string Method, string Uri) Of(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return (info.HttpMethod.ToString().ToUpperInvariant(), info.URI.AbsoluteUri);
    }

    [Fact]
    public void GetHitsItsRoute() => Assert.Equal(("GET", Base + "/members/m-1/sheet"), Of(Service().GetRequest("c-1", "m-1")));

    [Fact]
    public void UploadHitsItsRoute() => Assert.Equal(("POST", Base + "/members/m-1/sheet"), Of(Service().UploadRequest("c-1", "m-1", [1], "a.pdf")));

    [Fact]
    public void DeleteHitsItsRoute() => Assert.Equal(("DELETE", Base + "/members/m-1/sheet"), Of(Service().DeleteRequest("c-1", "m-1")));

    [Fact]
    public void FromSourceHitsItsRouteWithBody()
    {
        var info = Service().FromSourceRequest("c-1", "m-1", "file", "f-1");
        Assert.Equal(("POST", Base + "/members/m-1/sheet/duplicate"), Of(info));
        using var reader = new StreamReader(info.Content);
        var body = reader.ReadToEnd();
        Assert.Contains("\"source_type\":\"file\"", body);
        Assert.Contains("\"source_id\":\"f-1\"", body);
    }

    [Fact]
    public void SourcesHitsItsRoute() => Assert.Equal(("GET", Base + "/sheet-sources"), Of(Service().SourcesRequest("c-1")));

    [Fact]
    public void MimeMapsPdfAndFallsBackToImages()
    {
        Assert.Equal("application/pdf", CampaignMemberSheetService.SheetMimeForExtension("s.PDF"));
        Assert.Equal("image/png", CampaignMemberSheetService.SheetMimeForExtension("s.png"));
    }
}
