using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class MapsServiceTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    // An omitted filter must not reach the wire as an empty value: the server
    // treats `folder=` as "the root folder", which is a different query.
    [Fact]
    public void ListSendsOnlyTheFiltersThatWereGiven()
    {
        var info = Client().Api.Api.Maps.ToGetRequestInformation(c =>
        {
            c.QueryParameters.MapType = null;
            c.QueryParameters.Folder = null;
            c.QueryParameters.Limit = 100;
            c.QueryParameters.Offset = null;
        });
        var uri = Uri(info);
        Assert.Contains("limit=100", uri);
        Assert.DoesNotContain("folder", uri);
        Assert.DoesNotContain("map_type", uri);
        Assert.DoesNotContain("offset", uri);
    }

    [Fact]
    public void ListSendsEveryFilterWhenGiven()
    {
        var info = Client().Api.Api.Maps.ToGetRequestInformation(c =>
        {
            c.QueryParameters.MapType = "battlemap";
            c.QueryParameters.Folder = "battlemaps";
            c.QueryParameters.Limit = 5;
            c.QueryParameters.Offset = 10;
        });
        var uri = Uri(info);
        Assert.Contains("map_type=battlemap", uri);
        Assert.Contains("folder=battlemaps", uri);
        Assert.Contains("limit=5", uri);
        Assert.Contains("offset=10", uri);
    }

    // The folder endpoints are keyed by a path in the body, not by an id in the
    // URL — a regeneration that moved them under /maps would break every caller.
    [Fact]
    public void FolderRoutesAreTopLevelMapFolders()
    {
        var client = Client();
        Assert.EndsWith("/api/map-folders", Uri(client.Api.Api.MapFolders.ToGetRequestInformation()));
        Assert.EndsWith("/api/map-folders/bulk",
            Uri(client.Api.Api.MapFolders.Bulk.ToPostRequestInformation(
                new GrimoireCli.Generated.Models.BulkFolderTags())));
    }

    [Fact]
    public void BulkRoutesAreDistinctFromTheItemRoute()
    {
        var client = Client();
        Assert.EndsWith("/api/maps/bulk",
            Uri(client.Api.Api.Maps.Bulk.ToPostRequestInformation(
                new GrimoireCli.Generated.Models.MapBulkUpdate())));
        Assert.EndsWith("/api/maps/bulk/tags",
            Uri(client.Api.Api.Maps.Bulk.Tags.ToPostRequestInformation(
                new GrimoireCli.Generated.Models.BulkAddTags())));
        Assert.EndsWith("/api/maps/abc", Uri(client.Api.Api.Maps["abc"].ToGetRequestInformation()));
    }

    // The raw body is what makes a literal 0 survive to the server, which is the
    // documented way to clear a grid override. A model round-trip would drop it.
    [Fact]
    public void UpdateSendsTheRawBodyRatherThanTheModel()
    {
        var info = Client().Api.Api.Maps["abc"].ToPatchRequestInformation(
            new GrimoireCli.Generated.Models.MapUpdate());
        info.SetStreamContent(new MemoryStream("{\"grid_px\":0}"u8.ToArray()), "application/json");
        var body = new StreamReader(info.Content).ReadToEnd();
        Assert.Equal("{\"grid_px\":0}", body);
        Assert.Equal(Method.PATCH, info.HttpMethod);
    }
}
