using System.Net;
using System.Text;
using GrimoireCli.Api;
using NLog;
using NLog.Layouts;
using NLog.Targets;

namespace GrimoireCli.Tests.Api;

[Collection("NLog")]
public class DebugHttpHandlerTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string ResponseBody { get; init; } = "";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(Status)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private static MemoryTarget ConfigureMemoryTarget(bool debugEnabled)
    {
        var config = new NLog.Config.LoggingConfiguration();
        var target = new MemoryTarget("memory")
        {
            Layout = new SimpleLayout("${level:uppercase=true} ${message}")
        };
        config.AddTarget(target);
        config.AddRule(debugEnabled ? LogLevel.Debug : LogLevel.Warn, LogLevel.Fatal, target);
        LogManager.Configuration = config;
        return target;
    }

    [Fact]
    public async Task DebugOff_NoLines()
    {
        var target = ConfigureMemoryTarget(debugEnabled: false);
        var handler = new DebugHttpHandler(new StubHandler { Status = HttpStatusCode.OK, ResponseBody = "{}" });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };

        await client.GetAsync("foo");

        Assert.Empty(target.Logs);
    }

    [Fact]
    public async Task DebugOn_2xx_OneLineWithMethodUrlStatus()
    {
        var target = ConfigureMemoryTarget(debugEnabled: true);
        var handler = new DebugHttpHandler(new StubHandler { Status = HttpStatusCode.OK, ResponseBody = "{}" });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/sub/") };

        await client.GetAsync("api/items?expanded=1");

        Assert.Single(target.Logs);
        Assert.Equal("DEBUG GET https://example.com/sub/api/items?expanded=1 200", target.Logs[0]);
    }

    [Fact]
    public async Task DebugOn_Non2xx_TwoLinesIncludingResponseBody()
    {
        var target = ConfigureMemoryTarget(debugEnabled: true);
        var handler = new DebugHttpHandler(new StubHandler { Status = HttpStatusCode.BadRequest, ResponseBody = "{\"error\":\"nope\"}" });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };

        await client.PatchAsync("api/items/foo", new StringContent(""));

        Assert.Equal(2, target.Logs.Count);
        Assert.Equal("DEBUG PATCH https://example.com/api/items/foo 400", target.Logs[0]);
        Assert.Equal("DEBUG response body: {\"error\":\"nope\"}", target.Logs[1]);
    }

    [Fact]
    public async Task DebugOn_Non2xx_LongBody_TruncatedAt500()
    {
        var target = ConfigureMemoryTarget(debugEnabled: true);
        var longBody = new string('x', 600);
        var handler = new DebugHttpHandler(new StubHandler { Status = HttpStatusCode.InternalServerError, ResponseBody = longBody });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };

        await client.GetAsync("api/items");

        Assert.Equal(2, target.Logs.Count);
        Assert.Equal($"DEBUG response body: {new string('x', 500)}...", target.Logs[1]);
    }

    [Fact]
    public async Task DebugOn_Non2xx_BodyExactly500Chars_NotTruncated()
    {
        // Boundary the abs-cli suite never checks: the truncation cutoff is
        // "> 500", so a body of exactly 500 chars must pass through untouched,
        // with no trailing "...".
        var target = ConfigureMemoryTarget(debugEnabled: true);
        var exactBody = new string('y', 500);
        var handler = new DebugHttpHandler(new StubHandler { Status = HttpStatusCode.BadRequest, ResponseBody = exactBody });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };

        await client.GetAsync("api/items");

        Assert.Equal(2, target.Logs.Count);
        Assert.Equal($"DEBUG response body: {exactBody}", target.Logs[1]);
    }

    [Fact]
    public async Task DebugOn_Non2xx_ResponseBodyStillReadableByCaller()
    {
        // The handler reads response.Content itself to log it. If that read
        // consumed the stream, callers deserializing the body afterward would
        // see it empty. StringContent buffers internally, so this should be
        // safe — but abs-cli's suite never asserts it, and a future switch to
        // a streamed content type could silently break callers.
        var target = ConfigureMemoryTarget(debugEnabled: true);
        var handler = new DebugHttpHandler(new StubHandler { Status = HttpStatusCode.BadRequest, ResponseBody = "{\"error\":\"nope\"}" });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };

        var response = await client.GetAsync("api/items");
        var bodyAfterHandler = await response.Content.ReadAsStringAsync();

        Assert.Equal("{\"error\":\"nope\"}", bodyAfterHandler);
    }
}
