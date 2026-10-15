using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Pins each of the eight template requests to its route and method. Get, update
/// and delete share a path and differ only in method.
/// </summary>
public class CampaignWikiTemplateServiceTests
{
    private const string Base = "http://example.test/api/campaigns/c-1/wiki/templates";

    private static CampaignWikiTemplateService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static (string Method, string Uri) Of(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return (info.HttpMethod.ToString().ToUpperInvariant(), info.URI.AbsoluteUri);
    }

    public static TheoryData<string, string, string> Routes() => new()
    {
        { "list", "GET", Base },
        { "get", "GET", Base + "/t-1" },
        { "create", "POST", Base },
        { "update", "PATCH", Base + "/t-1" },
        { "delete", "DELETE", Base + "/t-1" },
        { "import", "POST", Base + "/upload" },
        { "export", "GET", Base + "/t-1/export" },
        { "use", "POST", Base + "/t-1/use" },
    };

    private static RequestInformation Request(string verb)
    {
        var s = Service();
        return verb switch
        {
            "list" => s.ListRequest("c-1"),
            "get" => s.GetRequest("c-1", "t-1"),
            "create" => s.CreateRequest("c-1", "{\"name\":\"N\"}"),
            "update" => s.UpdateRequest("c-1", "t-1", "{\"name\":\"N\"}"),
            "delete" => s.DeleteRequest("c-1", "t-1"),
            "import" => s.ImportRequest("c-1", [1], "npc.zip"),
            "export" => s.ExportRequest("c-1", "t-1"),
            "use" => s.UseRequest("c-1", "t-1"),
            _ => throw new ArgumentException($"Unknown verb '{verb}'.", nameof(verb)),
        };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void EachRequestHitsItsRouteWithItsMethod(string verb, string method, string uri)
        => Assert.Equal((method, uri), Of(Request(verb)));

    [Fact]
    public void CreateSendsTheRawBodyUnchanged()
    {
        var info = Service().CreateRequest("c-1", "{\"name\":\"NPC\",\"body\":\"# Hi\"}");
        using var reader = new StreamReader(info.Content);
        Assert.Equal("{\"name\":\"NPC\",\"body\":\"# Hi\"}", reader.ReadToEnd());
    }

    [Fact]
    public void ImportSendsOneFilePartNamedAfterTheLocalFile()
    {
        var info = Service().ImportRequest("c-1", "hello"u8.ToArray(), "npc.zip");
        using var reader = new StreamReader(info.Content);
        var body = reader.ReadToEnd();
        Assert.Contains("name=\"file\"", body);
        Assert.Contains("filename=\"npc.zip\"", body);
    }
}
