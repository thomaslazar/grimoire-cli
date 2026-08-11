using GrimoireCli.Api;
using GrimoireCli.Configuration;

namespace GrimoireCli.Tests.Api;

/// <summary>
/// The generated builders own URL construction. These pin the two behaviours the
/// deleted ApiEndpoints/QueryBuilder tests guarded: query-string encoding, and a
/// path parameter that cannot escape its segment.
/// </summary>
public class GeneratedClientTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    [Fact]
    public void ListBuildsQueryStringFromTypedParameters()
    {
        var info = Client().Api.Api.Systems.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Sort = "name";
            c.QueryParameters.IncludeChildren = true;
        });
        info.PathParameters["baseurl"] = "http://example.test";
        var uri = info.URI.AbsoluteUri;
        Assert.Contains("sort=name", uri);
        Assert.Contains("include_children=true", uri);
    }

    [Fact]
    public void AmpersandInAFilterValueIsEncoded()
    {
        var info = Client().Api.Api.Systems.ToGetRequestInformation(c =>
            c.QueryParameters.ParentSystem = "Dungeons & Dragons");
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Contains("Dungeons%20%26%20Dragons", info.URI.AbsoluteUri);
    }

    [Fact]
    public void APathParameterCannotEscapeItsSegment()
    {
        var info = Client().Api.Api.Systems["../about"].ToGetRequestInformation();
        info.PathParameters["baseurl"] = "http://example.test";
        var uri = info.URI.AbsoluteUri;
        Assert.DoesNotContain("../", uri);
        Assert.Contains("..%2Fabout", uri);
    }
}
