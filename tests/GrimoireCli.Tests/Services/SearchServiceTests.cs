using GrimoireCli.Api;
using GrimoireCli.Configuration;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// A Kiota regeneration can silently rename a wire query parameter (book_id
/// -> bookId) or move a path segment; the failure mode is a query parameter
/// the server ignores — unfiltered results with exit 0. Both endpoint paths
/// and every query-parameter wire name are pinned here.
/// </summary>
public class SearchServiceTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    [Fact]
    public void EachEndpointResolvesToItsOwnPath()
    {
        var api = Client().Api.Api.Search;
        Assert.Equal("http://example.test/api/search?q=", Uri(api.ToGetRequestInformation()));
        Assert.Equal("http://example.test/api/search/fields", Uri(api.Fields.ToGetRequestInformation()));
    }

    [Fact]
    public void SearchSendsAllFourQueryParameters()
    {
        var info = Client().Api.Api.Search.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Q = "dragon";
            c.QueryParameters.Limit = 50;
            c.QueryParameters.BookId = "book-1";
            c.QueryParameters.SystemId = "system-1";
        });
        var uri = Uri(info);
        Assert.Contains("q=", uri);
        Assert.Contains("limit=50", uri);
        Assert.Contains("book_id=", uri);
        Assert.Contains("system_id=", uri);
    }
}
