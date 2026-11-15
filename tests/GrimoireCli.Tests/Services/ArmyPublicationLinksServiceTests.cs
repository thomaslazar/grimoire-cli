using System.Web;
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class ArmyPublicationLinksServiceTests
{
    private const string Base = "http://example.test/api/armies/publication-links";

    private static ArmyPublicationLinksService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static (string Method, Uri Uri) Of(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return (info.HttpMethod.ToString().ToUpperInvariant(), info.URI);
    }

    [Fact]
    public void SetPutsToTheCollection()
    {
        var (method, uri) = Of(Service().SetRequest("Codex", "b-1", null, 0));
        Assert.Equal("PUT", method);
        Assert.Equal(Base, uri.GetLeftPart(UriPartial.Path));
    }

    [Fact]
    public void RemoveDeletesWithBothQueryParams()
    {
        var (method, uri) = Of(Service().RemoveRequest("Codex: Space Marines", "Warhammer 40,000"));
        var query = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("DELETE", method);
        Assert.Equal(Base, uri.GetLeftPart(UriPartial.Path));
        Assert.Equal("Codex: Space Marines", query["publication"]);
        Assert.Equal("Warhammer 40,000", query["system_name"]);
    }

    [Fact]
    public void BooksGetsWithQ()
    {
        var (method, uri) = Of(Service().BooksRequest("codex"));
        Assert.Equal("GET", method);
        Assert.Equal(Base + "/books?q=codex", uri.AbsoluteUri);
    }

    [Fact]
    public void SetBodyDefaultsPageOffsetToZero()
    {
        var body = ArmyPublicationLinksService.BuildSetBody("Codex", "b-1", null, 0);
        Assert.Equal(0, body.PageOffset);
        Assert.Equal("", body.SystemName);
    }
}
