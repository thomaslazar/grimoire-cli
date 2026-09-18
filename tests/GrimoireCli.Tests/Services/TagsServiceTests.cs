using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
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

    [Fact]
    public void EachWriteResolvesToItsOwnPath()
    {
        var api = Client().Api.Api.Tags;
        var create = new Generated.Models.TagCreate { Value = "dungeon" };
        Assert.Equal("http://example.test/api/tags", Uri(api.ToPostRequestInformation(create)));
        Assert.Contains("/api/tags/dungeon", Uri(api["dungeon"].ToDeleteRequestInformation()));
        Assert.Contains("/api/tags/dungeon", Uri(api["dungeon"].ToPatchRequestInformation(
            new Generated.Models.TagDisplayUpdate { Display = "Dungeon" })));
        Assert.Contains("/api/tags/dungeon/merge", Uri(api["dungeon"].Merge.ToPostRequestInformation(
            new Generated.Models.TagMerge { Into = "dungeons" })));
    }

    // display is a composed-type wrapper whose constructor sets nothing, so an
    // omitted --display must stay absent from the body rather than send null:
    // the server defaults it to the value's own trimmed casing.
    [Fact]
    public void OmittedDisplayLeavesTheCreateBodyWithoutIt()
    {
        var body = TagsService.BuildCreateBody("GM Screen", display: null);
        Assert.Equal("GM Screen", body.Value);
        Assert.Null(body.Display);
    }

    [Fact]
    public void GivenDisplayReachesTheCreateBodyThroughTheComposedWrapper()
    {
        var body = TagsService.BuildCreateBody("gm-screen", "GM Screen");
        Assert.Equal("GM Screen", body.Display!.String);
    }
}
