using System.Net;
using System.Text;
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

// EveryRawBodyCallSendsItsBodyUnchangedToItsOwnRoute sends through
// GrimoireApiClient's pipeline, and DebugHttpHandler sits in it — so this class
// writes into whatever global NLog target is configured at the time. Without the
// collection it races DebugHttpHandlerTests, whose assertions count the lines in
// that target.
[Collection("NLog")]
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

    // The builder-level tests above stop at the RequestInformation. This one goes
    // through MapsService, so a raw-body call that lost its SetStreamContent —
    // and would therefore send the empty model instead of what the caller typed —
    // fails here rather than reaching a server.
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string Body)> Seen { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null
                ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Seen.Add((request.Method, request.RequestUri!.AbsoluteUri, body));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }

    [Fact]
    public async Task EveryRawBodyCallSendsItsBodyUnchangedToItsOwnRoute()
    {
        var handler = new RecordingHandler();
        var dir = Directory.CreateTempSubdirectory().FullName;
        var config = new AppConfig
        {
            Server = "http://example.test",
            AccessToken = "t",
            // Keeps PreflightAsync from probing /api/about through the stub.
            LastVersionCheck = DateTimeOffset.UtcNow,
            LastServerVersion = "nightly"
        };
        var manager = new ConfigManager(Path.Combine(dir, "config.json"));
        manager.Save(config);
        var service = new MapsService(new GrimoireApiClient(config, manager, handler));

        await service.UpdateAsync("abc", "{\"grid_px\":0}");
        await service.BatchUpdateAsync("{\"items\":[{\"id\":\"abc\",\"grid_px\":70}]}");
        await service.BatchTagAsync("{\"ids\":[\"abc\"],\"tags\":[\"cave\"]}");
        await service.FoldersSetAsync("{\"path\":\"battlemaps\",\"tags\":[\"cave\"]}");
        await service.FoldersBatchSetAsync("{\"folders\":[{\"path\":\"battlemaps\",\"tags\":[]}]}");

        Assert.Equal(new[]
        {
            (HttpMethod.Patch, "http://example.test/api/maps/abc", "{\"grid_px\":0}"),
            (HttpMethod.Post, "http://example.test/api/maps/bulk",
                "{\"items\":[{\"id\":\"abc\",\"grid_px\":70}]}"),
            (HttpMethod.Post, "http://example.test/api/maps/bulk/tags",
                "{\"ids\":[\"abc\"],\"tags\":[\"cave\"]}"),
            (HttpMethod.Patch, "http://example.test/api/map-folders",
                "{\"path\":\"battlemaps\",\"tags\":[\"cave\"]}"),
            (HttpMethod.Post, "http://example.test/api/map-folders/bulk",
                "{\"folders\":[{\"path\":\"battlemaps\",\"tags\":[]}]}"),
        }, handler.Seen);
    }

    [Fact]
    public void ThumbnailResolvesToItsOwnRoute()
    {
        Assert.EndsWith("/api/maps/abc/thumbnail",
            Uri(Client().Api.Api.Maps["abc"].Thumbnail.ToGetRequestInformation()));
    }
}
