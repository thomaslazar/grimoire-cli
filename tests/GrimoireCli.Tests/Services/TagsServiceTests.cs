using GrimoireCli.Api;
using GrimoireCli.Configuration;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// A Kiota regeneration can silently rename a wire query parameter
/// (in_use_by -> inUseBy) or move a path segment; the failure mode is a
/// query parameter the server ignores — unfiltered results with exit 0.
/// Both endpoint paths and every query-parameter wire name are pinned here.
/// </summary>
public class TagsServiceTests
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
        var api = Client().Api.Api.Tags;
        Assert.Equal("http://example.test/api/tags", Uri(api.ToGetRequestInformation()));
        Assert.Contains("/api/tags/dungeon/items", Uri(api["dungeon"].Items.ToGetRequestInformation()));
    }

    [Fact]
    public void ListSendsInUseByAsAQueryParameter()
    {
        var info = Client().Api.Api.Tags.ToGetRequestInformation(c => c.QueryParameters.InUseBy = "book");
        Assert.Contains("in_use_by=", Uri(info));
    }

    [Fact]
    public void ItemsSendsResourceTypeAsAQueryParameter()
    {
        var info = Client().Api.Api.Tags["dungeon"].Items.ToGetRequestInformation(c => c.QueryParameters.ResourceType = "book");
        Assert.Contains("resource_type=", Uri(info));
    }
}
