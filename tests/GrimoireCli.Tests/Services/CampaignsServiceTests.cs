using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class CampaignsServiceTests
{
    private const string Base = "http://example.test/api/campaigns/c-1";

    private static CampaignsService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

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
        { "archive", "PUT", Base + "/archive" },
        { "convert", "POST", Base + "/convert-to-group" },
        { "delete", "DELETE", Base },
    };

    private static RequestInformation Request(string verb)
    {
        var s = Service();
        return verb switch
        {
            "archive" => s.ArchiveRequest("c-1", true),
            "convert" => s.ConvertToGroupRequest("c-1", null),
            "delete" => s.DeleteRequest("c-1"),
            _ => throw new ArgumentException($"Unknown verb '{verb}'.", nameof(verb)),
        };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void EachRequestHitsItsRouteWithItsMethod(string verb, string method, string uri)
        => Assert.Equal((method, uri), Of(Request(verb)));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ArchiveSendsTheTargetState(bool archived)
        => Assert.Contains($"\"archived\":{archived.ToString().ToLowerInvariant()}", Body(Service().ArchiveRequest("c-1", archived)));

    [Fact]
    public void ConvertOmitsGmTitleWhenAbsent()
        => Assert.DoesNotContain("gm_title", Body(Service().ConvertToGroupRequest("c-1", null)));

    [Fact]
    public void ConvertSendsGmTitleWhenGiven()
        => Assert.Contains("\"gm_title\":\"Dungeon Master\"", Body(Service().ConvertToGroupRequest("c-1", "Dungeon Master")));
}
