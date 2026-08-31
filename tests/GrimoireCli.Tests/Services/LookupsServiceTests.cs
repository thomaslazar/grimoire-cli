using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Pins each vocabulary to the path its generated builder produces. A client
/// regeneration that moves a builder would otherwise silently read the wrong
/// vocabulary, which no help text or response assertion would catch.
/// </summary>
public class LookupsServiceTests
{
    private static LookupsService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    [Theory]
    [InlineData("genres", "/api/genres")]
    [InlineData("licenses", "/api/licenses")]
    [InlineData("parent-systems", "/api/parent-systems")]
    [InlineData("system-families", "/api/system-families")]
    [InlineData("dice-materials", "/api/dice-materials")]
    public void EachVocabularyResolvesToItsOwnPath(string vocabulary, string expectedPath)
    {
        var info = Service().RequestFor(vocabulary);
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Equal("http://example.test" + expectedPath, info.URI.AbsoluteUri);
    }

    // The reads take no query parameters at all — the only one the spec declares
    // is `token`, the alternative auth scheme, which the CLI never uses because
    // the bearer header is set on the HttpClient.
    [Theory]
    [InlineData("genres")]
    [InlineData("dice-materials")]
    public void NoQueryStringIsSent(string vocabulary)
    {
        var info = Service().RequestFor(vocabulary);
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.DoesNotContain("?", info.URI.AbsoluteUri);
    }

    [Fact]
    public void AnUnknownVocabularyThrows()
    {
        Assert.Throws<ArgumentException>(() => Service().RequestFor("tags"));
    }
}
