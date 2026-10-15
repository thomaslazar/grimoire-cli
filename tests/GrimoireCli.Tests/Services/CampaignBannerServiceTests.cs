using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class CampaignBannerServiceTests
{
    private const string Base = "http://example.test/api/campaigns/c-1/banner";

    private static GrimoireApiClient Client() => new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static (string Method, string Uri) Of(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return (info.HttpMethod.ToString().ToUpperInvariant(), info.URI.AbsoluteUri);
    }

    public static TheoryData<string, string, string> Routes() => new()
    {
        { "get", "GET", Base },
        { "get-full", "GET", Base + "?size=full" },
        { "upload", "POST", Base },
        { "delete", "DELETE", Base },
        { "from-source", "POST", Base + "/from-source" },
        { "focus", "PUT", Base + "/focus" },
        { "image", "POST", "http://example.test/api/campaigns/c-1/images" },
    };

    private static RequestInformation Request(string verb)
    {
        var s = new CampaignBannerService(Client());
        return verb switch
        {
            "get" => s.GetRequest("c-1", false),
            "get-full" => s.GetRequest("c-1", true),
            "upload" => s.UploadRequest("c-1", [1], "b.png"),
            "delete" => s.DeleteRequest("c-1"),
            "from-source" => s.FromSourceRequest("c-1", "map", "m-1"),
            "focus" => s.FocusRequest("c-1", 30),
            "image" => new CampaignFilesService(Client()).UploadImageRequest("c-1", [1], "i.png", null, null),
            _ => throw new ArgumentException(verb),
        };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void RequestHitsItsRoute(string verb, string method, string uri)
        => Assert.Equal((method, uri), Of(Request(verb)));

    private static string Body(RequestInformation info)
    {
        using var reader = new StreamReader(info.Content);
        return reader.ReadToEnd();
    }

    [Fact]
    public void FocusSendsFocusY() => Assert.Contains("\"focus_y\":30", Body(Request("focus")).Replace(" ", ""));

    [Fact]
    public void FromSourceSendsTypeAndId()
    {
        var body = Body(Request("from-source")).Replace(" ", "");
        Assert.Contains("\"source_type\":\"map\"", body);
        Assert.Contains("\"source_id\":\"m-1\"", body);
    }

    [Theory]
    [InlineData("upload")]
    [InlineData("image")]
    public void UploadSendsAnImagePartNamedFile(string verb)
    {
        var body = Body(Request(verb));
        Assert.Contains("name=\"file\"", body);
        Assert.Contains("Content-Type: image/png", body);
    }
}
