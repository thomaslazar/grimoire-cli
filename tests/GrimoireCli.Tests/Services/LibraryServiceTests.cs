using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class LibraryServiceTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    // The generated RescanRequest constructor sets MetadataMode to New
    // unconditionally, unlike Scope which it leaves untouched. Pins that
    // BuildBody nulls it back out when --metadata-mode is omitted, so a client
    // regeneration can't silently reintroduce a client-side default the CLI
    // never documents (thin pass-through: the server's own default applies
    // instead).
    [Fact]
    public void OmittedMetadataModeLeavesTheRequestFieldNull()
    {
        var body = LibraryService.BuildBody(scope: null, metadataMode: null);
        Assert.Null(body.MetadataMode);
    }

    [Fact]
    public void GivenMetadataModeSetsTheEnumValue()
    {
        var body = LibraryService.BuildBody(scope: null, metadataMode: "replace");
        Assert.Equal(GrimoireCli.Generated.Models.RescanRequest_metadata_mode.Replace, body.MetadataMode);
    }

    [Fact]
    public void OmittedScopeLeavesTheRequestFieldNull()
    {
        var body = LibraryService.BuildBody(scope: null, metadataMode: null);
        Assert.Null(body.Scope);
    }

    [Fact]
    public void GivenScopeSetsTheStringBranchOfTheWrapper()
    {
        var body = LibraryService.BuildBody(scope: "books/D&D 5e", metadataMode: null);
        Assert.Equal("books/D&D 5e", body.Scope?.String);
    }

    [Fact]
    public void StatsResolvesToTheStatsPath()
    {
        var info = Client().Api.Api.Stats.ToGetRequestInformation();
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Equal("http://example.test/api/stats", info.URI.AbsoluteUri);
    }
}
