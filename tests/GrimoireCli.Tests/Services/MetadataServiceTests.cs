using GrimoireCli.Services;

namespace GrimoireCli.Tests.Services;

public class MetadataServiceTests
{
    // The generated model's properties are plain strings whose constructor sets
    // none of them, so an omitted flag must stay unset rather than be sent as "".
    // Pinned here because a client regeneration could change that quietly.
    [Fact]
    public void FetchBodyOmitsWhatWasNotGiven()
    {
        var body = MetadataService.BuildFetchBody("fixture-source", identity: "abc",
            query: null, paste: null);
        Assert.Equal("fixture-source", body.SourceId);
        Assert.Equal("abc", body.Identity);
        Assert.Null(body.Query);
        Assert.Null(body.Paste);
    }

    [Fact]
    public void FetchBodyCarriesPasteInsteadOfIdentity()
    {
        var body = MetadataService.BuildFetchBody("fixture-source", identity: null,
            query: null, paste: "https://fixture.test/systems/shadowrun-4-de");
        Assert.Null(body.Identity);
        Assert.Equal("https://fixture.test/systems/shadowrun-4-de", body.Paste);
    }

    // An unknown resource is a programming error, not user input: the only two
    // callers pass a literal.
    [Fact]
    public void AnUnknownResourceIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new MetadataService(null!, "maps"));
    }
}
