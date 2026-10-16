using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;

namespace GrimoireCli.Tests.Services;

public class CampaignFilesServiceTests
{
    // The server sets is_image from this type (uploads.py:589).
    [Theory]
    [InlineData("map.PNG", "image/png")]
    [InlineData("a.jpeg", "image/jpeg")]
    [InlineData("a.webp", "image/webp")]
    [InlineData("a.gif", "image/gif")]
    [InlineData("handout.pdf", "application/pdf")]
    [InlineData("scene.uvtt", "application/octet-stream")]
    public void MimeForExtension(string path, string expected)
        => Assert.Equal(expected, CampaignFilesService.MimeForExtension(path));

    [Fact]
    public void GetHitsItsRoute()
    {
        var info = new CampaignFilesService(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" })).GetRequest("c-1", "f-1");
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Equal(("GET", "http://example.test/api/campaigns/c-1/files/f-1"), (info.HttpMethod.ToString().ToUpperInvariant(), info.URI.AbsoluteUri));
    }
}
