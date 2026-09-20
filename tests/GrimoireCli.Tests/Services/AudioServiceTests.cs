using System.Net;
using System.Text;
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

// The raw-body test sends through GrimoireApiClient's pipeline, and
// DebugHttpHandler sits in it — so this class writes into whatever global NLog
// target is configured at the time. Without the collection it races
// DebugHttpHandlerTests, whose assertions count the lines in that target.
[Collection("NLog")]
public class AudioServiceTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    // An omitted flag must not reach the wire: the server's own default is what
    // should apply, not an empty value.
    [Fact]
    public void ListSendsOnlyThePagingItWasGiven()
    {
        var info = Client().Api.Api.Audio.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Limit = 100;
            c.QueryParameters.Offset = null;
        });
        var uri = Uri(info);
        Assert.Contains("limit=100", uri);
        Assert.DoesNotContain("offset", uri);
    }

    // The folder endpoints are keyed by a path in the body, not by an id in the
    // URL — a regeneration that moved them under /audio would break callers.
    [Fact]
    public void FolderRoutesAreTopLevelAudioFolders()
    {
        var client = Client();
        Assert.EndsWith("/api/audio-folders", Uri(client.Api.Api.AudioFolders.ToGetRequestInformation()));
        Assert.EndsWith("/api/audio-folders/bulk",
            Uri(client.Api.Api.AudioFolders.Bulk.ToPostRequestInformation(
                new GrimoireCli.Generated.Models.BulkFolderTags())));
    }

    [Fact]
    public void BulkRoutesAreDistinctFromTheItemRoute()
    {
        var client = Client();
        Assert.EndsWith("/api/audio/bulk",
            Uri(client.Api.Api.Audio.Bulk.ToPostRequestInformation(
                new GrimoireCli.Generated.Models.AudioBulkUpdate())));
        Assert.EndsWith("/api/audio/bulk/tags",
            Uri(client.Api.Api.Audio.Bulk.Tags.ToPostRequestInformation(
                new GrimoireCli.Generated.Models.BulkAddTags())));
        Assert.EndsWith("/api/audio/abc", Uri(client.Api.Api.Audio["abc"].ToGetRequestInformation()));
        Assert.EndsWith("/api/audio/abc/artwork",
            Uri(client.Api.Api.Audio["abc"].Artwork.ToGetRequestInformation()));
    }

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

    // Goes through AudioService itself rather than stopping at the builder, so a
    // raw-body call that lost its SetStreamContent — and would therefore send the
    // empty model instead of what the caller typed — fails here.
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
        var service = new AudioService(new GrimoireApiClient(config, manager, handler));

        await service.UpdateAsync("abc", "{\"description\":\"tavern loop\"}");
        await service.BatchUpdateAsync("{\"items\":[{\"id\":\"abc\",\"description\":\"x\"}]}");
        await service.BatchTagAsync("{\"ids\":[\"abc\"],\"tags\":[\"ambience\"]}");
        await service.FoldersSetAsync("{\"path\":\"Ambience\",\"tags\":[\"ambience\"]}");
        await service.FoldersBatchSetAsync("{\"folders\":[{\"path\":\"Ambience\",\"tags\":[]}]}");

        Assert.Equal(new[]
        {
            (HttpMethod.Patch, "http://example.test/api/audio/abc", "{\"description\":\"tavern loop\"}"),
            (HttpMethod.Post, "http://example.test/api/audio/bulk", "{\"items\":[{\"id\":\"abc\",\"description\":\"x\"}]}"),
            (HttpMethod.Post, "http://example.test/api/audio/bulk/tags", "{\"ids\":[\"abc\"],\"tags\":[\"ambience\"]}"),
            (HttpMethod.Patch, "http://example.test/api/audio-folders", "{\"path\":\"Ambience\",\"tags\":[\"ambience\"]}"),
            (HttpMethod.Post, "http://example.test/api/audio-folders/bulk", "{\"folders\":[{\"path\":\"Ambience\",\"tags\":[]}]}"),
        }, handler.Seen);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void EachCoverRouteResolvesToItsOwnPath()
    {
        var api = Client().Api.Api.Audio["a1"];
        Assert.Contains("/api/audio/a1/cover", Uri(api.Cover.ToGetRequestInformation()));
        Assert.Contains("/api/audio/a1/cover", Uri(api.Cover.ToDeleteRequestInformation()));
        Assert.Contains("/api/audio/a1/cover/from-source", Uri(api.Cover.FromSource.ToPostRequestInformation(
            new Generated.Models.AudioCoverSourceIn { SourceType = "book", SourceId = "b1" })));
    }

    // The server reads the part by name; a rename would upload nothing and the
    // failure would look like a validation error rather than a client bug.
    [Fact]
    public void TheUploadPartIsNamedFile()
    {
        var body = AudioService.BuildCoverUploadBody(new byte[] { 1, 2, 3 }, "cover.png");
        Assert.Equal(new byte[] { 1, 2, 3 }, body.GetPartValue<byte[]>("file", "cover.png"));
    }
}
