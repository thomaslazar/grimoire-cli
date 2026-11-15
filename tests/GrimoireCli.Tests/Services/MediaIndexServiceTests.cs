using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Pins each collection's index request to its own /index path: the four
/// services are near-identical copies, and every response has the same shape,
/// so a builder left unswapped would query another collection unnoticed.
/// </summary>
public class MediaIndexServiceTests
{
    private static readonly MediaBrowseFilter NoFilter = new(null, null, null, null, null, null, null);

    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    private static RequestInformation Request(string collection, MediaBrowseFilter filter)
    {
        var client = Client();
        return collection switch
        {
            "maps" => new MapsService(client).IndexRequest(null, filter),
            "tokens" => new TokensService(client).IndexRequest(filter),
            "audio" => new AudioService(client).IndexRequest(filter),
            "models" => new ModelsService(client).IndexRequest(filter),
            _ => throw new ArgumentException($"Unknown collection '{collection}'.", nameof(collection)),
        };
    }

    [Theory]
    [InlineData("maps")]
    [InlineData("tokens")]
    [InlineData("audio")]
    [InlineData("models")]
    public void EachCollectionResolvesToItsOwnIndexRoute(string collection)
        => Assert.Equal($"http://example.test/api/{collection}/index", Uri(Request(collection, NoFilter)));

    [Theory]
    [InlineData("maps")]
    [InlineData("tokens")]
    [InlineData("audio")]
    [InlineData("models")]
    public void EachCollectionSendsEveryFilter(string collection)
    {
        var uri = Uri(Request(collection, new MediaBrowseFilter(
            "forest", "[\"x\"]", true, "2026-01-01T00:00:00Z", "battlemaps", "name", "desc")));
        foreach (var part in new[] { "q=forest", "tags=", "favorites=true", "added_since=", "folder=battlemaps", "sort=name", "order=desc" })
            Assert.Contains(part, uri);
    }

    [Fact]
    public void MapsSendsMapType()
        => Assert.Contains("map_type=battlemap",
            Uri(new MapsService(Client()).IndexRequest("battlemap", NoFilter)));
}
