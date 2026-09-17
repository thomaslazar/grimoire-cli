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
public class ModelsServiceTests
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
        var info = Client().Api.Api.Models.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Limit = 100;
            c.QueryParameters.Offset = null;
        });
        var uri = Uri(info);
        Assert.Contains("limit=100", uri);
        Assert.DoesNotContain("offset", uri);
    }

    // The folder endpoints are keyed by a path in the body, not by an id in the
    // URL — a regeneration that moved them under /models would break callers.
    [Fact]
    public void FolderRoutesAreTopLevelModelFolders()
    {
        var client = Client();
        Assert.EndsWith("/api/model-folders", Uri(client.Api.Api.ModelFolders.ToGetRequestInformation()));
        Assert.EndsWith("/api/model-folders/bulk",
            Uri(client.Api.Api.ModelFolders.Bulk.ToPostRequestInformation(
                new GrimoireCli.Generated.Models.BulkFolderTags())));
    }

    [Fact]
    public void BulkRoutesAreDistinctFromTheItemRoute()
    {
        var client = Client();
        Assert.EndsWith("/api/models/bulk",
            Uri(client.Api.Api.Models.Bulk.ToPostRequestInformation(
                new GrimoireCli.Generated.Models.Model3DBulkUpdate())));
        Assert.EndsWith("/api/models/bulk/tags",
            Uri(client.Api.Api.Models.Bulk.Tags.ToPostRequestInformation(
                new GrimoireCli.Generated.Models.BulkAddTags())));
        Assert.EndsWith("/api/models/abc", Uri(client.Api.Api.Models["abc"].ToGetRequestInformation()));
        Assert.EndsWith("/api/models/abc/thumbnail",
            Uri(client.Api.Api.Models["abc"].Thumbnail.ToGetRequestInformation()));
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

    // Goes through ModelsService itself rather than stopping at the builder, so a
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
        var service = new ModelsService(new GrimoireApiClient(config, manager, handler));

        await service.UpdateAsync("abc", "{\"is_supported\":true}");
        await service.BatchUpdateAsync("{\"items\":[{\"id\":\"abc\",\"is_explicit\":false}]}");
        await service.BatchTagAsync("{\"ids\":[\"abc\"],\"tags\":[\"goblin\"]}");
        await service.FoldersSetAsync("{\"path\":\"Goblins\",\"tags\":[\"goblin\"]}");
        await service.FoldersBatchSetAsync("{\"folders\":[{\"path\":\"Goblins\",\"tags\":[]}]}");

        Assert.Equal(new[]
        {
            (HttpMethod.Patch, "http://example.test/api/models/abc", "{\"is_supported\":true}"),
            (HttpMethod.Post, "http://example.test/api/models/bulk", "{\"items\":[{\"id\":\"abc\",\"is_explicit\":false}]}"),
            (HttpMethod.Post, "http://example.test/api/models/bulk/tags", "{\"ids\":[\"abc\"],\"tags\":[\"goblin\"]}"),
            (HttpMethod.Patch, "http://example.test/api/model-folders", "{\"path\":\"Goblins\",\"tags\":[\"goblin\"]}"),
            (HttpMethod.Post, "http://example.test/api/model-folders/bulk", "{\"folders\":[{\"path\":\"Goblins\",\"tags\":[]}]}"),
        }, handler.Seen);
        Directory.Delete(dir, recursive: true);
    }
}
