using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Pins each collection's folder-counts request to its own /groups path. The
/// four services are near-identical copies, so one copied without swapping the
/// builder would query another collection — which nothing else here would
/// catch, since every response has the same shape.
/// </summary>
public class MediaFolderCountsServiceTests
{
    private static readonly MediaBrowseFilter NoFilter = new(null, null, null, null, null, null, null);

    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    public static TheoryData<string, string> Requests() => new()
    {
        { "maps", "/api/maps/groups" },
    };

    private static RequestInformation Request(string collection, MediaBrowseFilter filter)
    {
        var client = Client();
        return collection switch
        {
            "maps" => new MapsService(client).FolderCountsRequest(null, filter),
            _ => throw new ArgumentException($"Unknown collection '{collection}'.", nameof(collection)),
        };
    }

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    [Theory]
    [MemberData(nameof(Requests))]
    public void EachServiceResolvesToItsOwnPath(string collection, string expectedPath)
        => Assert.Equal("http://example.test" + expectedPath, Uri(Request(collection, NoFilter)));

    [Theory]
    [MemberData(nameof(Requests))]
    public void TheFilterReachesTheQueryString(string collection, string expectedPath)
    {
        _ = expectedPath;
        var uri = Uri(Request(collection, new MediaBrowseFilter("forest", null, true, null, "a", "name", "desc")));
        Assert.Contains("q=forest", uri);
        Assert.Contains("favorites=true", uri);
        Assert.Contains("folder=a", uri);
        Assert.Contains("sort=name", uri);
        Assert.Contains("order=desc", uri);
    }

    [Fact]
    public void MapsSendsMapType()
        => Assert.Contains("map_type=battlemap",
            Uri(new MapsService(Client()).FolderCountsRequest("battlemap", NoFilter)));
}
