using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Pins each of the twelve wiki requests to its route and method. Several share a
/// path and differ only in method (get/update/delete a page; hide/unhide), so a
/// builder call swapped for its neighbour would hit the right URL with the
/// wrong verb — which nothing else here would catch.
/// </summary>
public class CampaignWikiServiceTests
{
    private const string Base = "http://example.test/api/campaigns/c-1/wiki";

    private static CampaignWikiService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static (string Method, string Uri) Of(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return (info.HttpMethod.ToString().ToUpperInvariant(), info.URI.AbsoluteUri);
    }

    public static TheoryData<string, string, string> Routes() => new()
    {
        { "list", "GET", Base },
        { "get", "GET", Base + "/p-1" },
        { "create", "POST", Base },
        { "update", "PATCH", Base + "/p-1" },
        { "delete", "DELETE", Base + "/p-1" },
        { "search", "GET", Base + "/search?q=ruins" },
        { "titles", "GET", Base + "/titles" },
        { "reorder", "PUT", Base + "/reorder" },
        { "hide", "POST", Base + "/p-1/hide" },
        { "unhide", "DELETE", Base + "/p-1/hide" },
        { "export", "GET", Base + "/export" },
        { "import", "POST", Base + "/import" },
    };

    private static RequestInformation Request(string verb)
    {
        var s = Service();
        return verb switch
        {
            "list" => s.ListRequest("c-1", false, false),
            "get" => s.GetRequest("c-1", "p-1"),
            "create" => s.CreateRequest("c-1", "{\"title\":\"T\"}"),
            "update" => s.UpdateRequest("c-1", "p-1", "{\"title\":\"T\"}"),
            "delete" => s.DeleteRequest("c-1", "p-1"),
            "search" => s.SearchRequest("c-1", "ruins"),
            "titles" => s.TitlesRequest("c-1"),
            "reorder" => s.ReorderRequest("c-1", ["p-2", "p-1"]),
            "hide" => s.HideRequest("c-1", "p-1"),
            "unhide" => s.UnhideRequest("c-1", "p-1"),
            "export" => s.ExportRequest("c-1", null),
            "import" => s.ImportRequest("c-1", [1], "vault.zip"),
            _ => throw new ArgumentException($"Unknown verb '{verb}'.", nameof(verb)),
        };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void EachRequestHitsItsRouteWithItsMethod(string verb, string method, string uri)
        => Assert.Equal((method, uri), Of(Request(verb)));

    [Fact]
    public void ListSendsNoFlagsByDefault()
        => Assert.DoesNotContain("?", Of(Service().ListRequest("c-1", false, false)).Uri);

    [Fact]
    public void ListSendsMineAndIncludeHiddenWhenSet()
    {
        var uri = Of(Service().ListRequest("c-1", true, true)).Uri;
        Assert.Contains("mine=true", uri);
        Assert.Contains("include_hidden=true", uri);
    }

    [Fact]
    public void CreateSendsTheRawBodyUnchanged()
    {
        var info = Service().CreateRequest("c-1", "{\"title\":\"Baron\",\"body\":\"# Hi\"}");
        using var reader = new StreamReader(info.Content);
        Assert.Equal("{\"title\":\"Baron\",\"body\":\"# Hi\"}", reader.ReadToEnd());
    }

    [Fact]
    public void ReorderSendsTheIdsInOrder()
    {
        var info = Service().ReorderRequest("c-1", ["p-2", "p-1"]);
        using var reader = new StreamReader(info.Content);
        Assert.Contains("\"ordered_ids\":[\"p-2\",\"p-1\"]", reader.ReadToEnd().Replace(" ", ""));
    }

    [Fact]
    public void ExportSendsTheFormatOnlyWhenSet()
    {
        Assert.DoesNotContain("?", Of(Service().ExportRequest("c-1", null)).Uri);
        Assert.EndsWith("/export?format=json", Of(Service().ExportRequest("c-1", "json")).Uri);
    }

    [Fact]
    public void ImportSendsOneFilePartNamedAfterTheLocalFile()
    {
        var info = Service().ImportRequest("c-1", "hello"u8.ToArray(), "vault.zip");
        using var reader = new StreamReader(info.Content);
        var body = reader.ReadToEnd();
        Assert.Contains("name=\"file\"", body);
        Assert.Contains("filename=\"vault.zip\"", body);
    }
}
