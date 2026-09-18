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

    private static RequestInformation CreateRequest(string vocabulary)
    {
        var client = Client();
        return vocabulary switch
        {
            "genres" => new GenresService(client).CreateRequest("Solo", parentId: null),
            "licenses" => new LicensesService(client).CreateRequest("OGL"),
            "parent-systems" => new ParentSystemsService(client).CreateRequest("D20"),
            "system-families" => new SystemFamiliesService(client).CreateRequest("DSA"),
            "dice-materials" => new DiceMaterialsService(client).CreateRequest("Oak", group: null),
            _ => throw new ArgumentException($"Unknown vocabulary '{vocabulary}'.", nameof(vocabulary)),
        };
    }

    private static RequestInformation DeleteRequest(string vocabulary, bool force)
    {
        var client = Client();
        return vocabulary switch
        {
            "genres" => new GenresService(client).DeleteRequest("v1", force),
            "licenses" => new LicensesService(client).DeleteRequest("v1", force),
            "parent-systems" => new ParentSystemsService(client).DeleteRequest("v1", force),
            "system-families" => new SystemFamiliesService(client).DeleteRequest("v1", force),
            "dice-materials" => new DiceMaterialsService(client).DeleteRequest("v1", force),
            _ => throw new ArgumentException($"Unknown vocabulary '{vocabulary}'.", nameof(vocabulary)),
        };
    }

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    // A service copied from its neighbour without swapping the builder would
    // write to the wrong vocabulary, which nothing else catches: every one of
    // these responses has the same shape.
    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void EachCreateResolvesToItsOwnPath(string vocabulary, string expectedPath)
    {
        Assert.Equal("http://example.test" + expectedPath, Uri(CreateRequest(vocabulary)));
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void EachDeleteResolvesToItsOwnPath(string vocabulary, string expectedPath)
    {
        Assert.Contains(expectedPath + "/v1", Uri(DeleteRequest(vocabulary, force: false)));
    }

    // force is the one query parameter these writes send; a regeneration that
    // renamed it would leave a flag the server ignores — a delete that 409s
    // when the caller asked it not to.
    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void DeleteSendsForceAsAQueryParameter(string vocabulary, string expectedPath)
    {
        _ = expectedPath;
        Assert.Contains("force=true", Uri(DeleteRequest(vocabulary, force: true)));
        Assert.Contains("force=false", Uri(DeleteRequest(vocabulary, force: false)));
    }

    // Both are composed-type wrappers whose constructors set nothing, so an
    // omitted flag must stay absent from the body: the server defaults group to
    // "Custom", and a null parent_id is what makes a top-level genre.
    [Fact]
    public void OmittedGenreParentLeavesTheBodyWithoutIt()
    {
        var body = GenresService.BuildCreateBody("Solo", parentId: null);
        Assert.Equal("Solo", body.Name);
        Assert.Null(body.ParentId);
    }

    [Fact]
    public void GivenGenreParentReachesTheBodyThroughTheComposedWrapper()
    {
        Assert.Equal("g1", GenresService.BuildCreateBody("Solo", "g1").ParentId!.String);
    }

    [Fact]
    public void OmittedDiceGroupLeavesTheBodyWithoutIt()
    {
        var body = DiceMaterialsService.BuildCreateBody("Oak", group: null);
        Assert.Equal("Oak", body.Name);
        Assert.Null(body.Group);
    }

    [Fact]
    public void GivenDiceGroupReachesTheBodyThroughTheComposedWrapper()
    {
        Assert.Equal("Wood", DiceMaterialsService.BuildCreateBody("Oak", "Wood").Group!.String);
    }
}
