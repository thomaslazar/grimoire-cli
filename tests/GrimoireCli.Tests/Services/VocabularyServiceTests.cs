using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Pins each vocabulary service to the path its generated builder produces. A
/// client regeneration that moves a builder, or a service copied from its
/// neighbour without swapping the builder, would otherwise read the wrong
/// vocabulary — which nothing else here would catch, since every response has
/// the same shape.
/// </summary>
public class VocabularyServiceTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    public static TheoryData<string, string> Vocabularies() => new()
    {
        { "genres", "/api/genres" },
        { "licenses", "/api/licenses" },
        { "parent-systems", "/api/parent-systems" },
        { "system-families", "/api/system-families" },
        { "dice-materials", "/api/dice-materials" },
    };

    private static RequestInformation ListRequest(string vocabulary)
    {
        var client = Client();
        return vocabulary switch
        {
            "genres" => new GenresService(client).ListRequest(),
            "licenses" => new LicensesService(client).ListRequest(),
            "parent-systems" => new ParentSystemsService(client).ListRequest(),
            "system-families" => new SystemFamiliesService(client).ListRequest(),
            "dice-materials" => new DiceMaterialsService(client).ListRequest(),
            _ => throw new ArgumentException($"Unknown vocabulary '{vocabulary}'.", nameof(vocabulary)),
        };
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void EachServiceResolvesToItsOwnPath(string vocabulary, string expectedPath)
    {
        var info = ListRequest(vocabulary);
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Equal("http://example.test" + expectedPath, info.URI.AbsoluteUri);
    }

    // The reads take no query parameters at all — the only one the spec declares
    // is `token`, the alternative auth scheme, which the CLI never uses because
    // the bearer header is set on the HttpClient.
    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void NoQueryStringIsSent(string vocabulary, string expectedPath)
    {
        _ = expectedPath;
        var info = ListRequest(vocabulary);
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.DoesNotContain("?", info.URI.AbsoluteUri);
    }
}
