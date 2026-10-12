using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Pins the three shelf requests to their own routes under the system, and
/// checks the shelf filters reach the query string under the server's names.
/// </summary>
public class SystemBooksServiceTests
{
    private static readonly ShelfBrowseFilter NoFilter =
        new(null, null, null, null, null, null, null, null, null, null, null);

    private static SystemBooksService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    [Fact]
    public void ListResolvesToTheBooksRoute()
        => Assert.Equal("http://example.test/api/systems/sys-1/books",
            Uri(Service().ListRequest("sys-1", null, null, NoFilter)));

    [Fact]
    public void FolderCountsResolvesToTheBookGroupsRoute()
        => Assert.Equal("http://example.test/api/systems/sys-1/book-groups",
            Uri(Service().FolderCountsRequest("sys-1", NoFilter)));

    [Fact]
    public void FacetsResolvesToTheBookFacetsRoute()
        => Assert.Equal("http://example.test/api/systems/sys-1/book-facets",
            Uri(Service().FacetsRequest("sys-1")));

    [Fact]
    public void ListSendsPagingAndEveryFilter()
    {
        var uri = Uri(Service().ListRequest("sys-1", 50, 100, new ShelfBrowseFilter(
            "kiesow", "[\"x\"]", true, "2026-01-01T00:00:00Z", false, "fantasy", "PZO", "core", "errata", "product_code", "desc")));
        foreach (var part in new[] { "limit=50", "offset=100", "q=kiesow", "tags=", "added_since=", "favorites=true", "explicit=false",
                     "genre=fantasy", "product_code=PZO", "category=core", "folder=errata",
                     "sort=product_code", "order=desc" })
            Assert.Contains(part, uri);
    }

    [Fact]
    public void FolderCountsSendsEveryFilter()
    {
        var uri = Uri(Service().FolderCountsRequest("sys-1", new ShelfBrowseFilter(
            "kiesow", "[\"x\"]", true, "2026-01-01T00:00:00Z", false, "fantasy", "PZO", "core", "errata", "year", "desc")));
        foreach (var part in new[] { "q=kiesow", "tags=", "favorites=true", "added_since=", "explicit=false",
                     "genre=fantasy", "product_code=PZO", "category=core", "folder=errata",
                     "sort=year", "order=desc" })
            Assert.Contains(part, uri);
    }

    [Fact]
    public void FolderCountsSendsTheSortThatDrivesFirst()
    {
        var uri = Uri(Service().FolderCountsRequest("sys-1", NoFilter with { Sort = "year", Order = "asc" }));
        Assert.Contains("sort=year", uri);
        Assert.Contains("order=asc", uri);
    }
}
