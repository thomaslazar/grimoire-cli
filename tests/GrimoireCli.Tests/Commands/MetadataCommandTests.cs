using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class MetadataCommandTests
{
    private static string SystemsHelp(string leaf, bool full) =>
        HelpRenderer.Render(SystemsCommand.Create(), ["systems", leaf], full);

    private static string BooksHelp(string leaf, bool full) =>
        HelpRenderer.Render(BooksCommand.Create(), ["books", leaf], full);

    [Theory]
    [InlineData("metadata-sources")]
    [InlineData("metadata-search")]
    [InlineData("metadata-fetch")]
    public void EveryCommandExistsOnBothResources(string leaf)
    {
        Assert.Contains(leaf, SystemsHelp(leaf, full: false));
        Assert.Contains(leaf, BooksHelp(leaf, full: false));
    }

    // All six routes are require_gm_or_admin.
    [Theory]
    [InlineData("metadata-sources")]
    [InlineData("metadata-search")]
    [InlineData("metadata-fetch")]
    public void EveryCommandIsTaggedGmOrAdmin(string leaf)
    {
        Assert.Contains("gm or admin", SystemsHelp(leaf, full: false));
        Assert.Contains("gm or admin", BooksHelp(leaf, full: false));
    }

    // The empty-sources case has three distinct causes and only one of them is
    // "no add-on installed", so help must point at where it is diagnosed.
    [Fact]
    public void SourcesExplainsWhyTheListIsEmpty()
    {
        var output = SystemsHelp("metadata-sources", full: false);
        Assert.Contains("addons list", output);
        Assert.Contains("targets this resource type", output);
        Assert.Contains("supports_paste", output);
    }

    // The fallback noun is the only wording that differs between the two sets.
    [Fact]
    public void SearchNamesTheResourcesOwnFallback()
    {
        Assert.Contains("defaults to the name", SystemsHelp("metadata-search", full: false));
        Assert.Contains("defaults to the title", BooksHelp("metadata-search", full: false));
    }

    [Fact]
    public void FetchSaysItWritesNothingAndNamesTheApplyPath()
    {
        var output = SystemsHelp("metadata-fetch", full: false);
        Assert.Contains("Writes nothing", output);
        Assert.Contains("systems update", output);
        Assert.Contains("only_incoming", output);
        Assert.Contains("union with the existing list", output);
    }

    [Fact]
    public void FetchNamesBooksUpdateOnBooks()
    {
        Assert.Contains("books update", BooksHelp("metadata-fetch", full: false));
    }

    // fetch never substitutes a fallback for an omitted --query — only search
    // does — so the flag description must not claim otherwise.
    [Fact]
    public void FetchQueryDescriptionDoesNotClaimAFallback()
    {
        var output = SystemsHelp("metadata-fetch", full: true);
        Assert.Contains("required for search-backed sources", output);
        Assert.DoesNotContain("defaults to the", output);
    }

    // character_builder_urls is not a mappable book field (manifest.py), so
    // books' help must not carry a caveat that can never apply to it.
    [Fact]
    public void FetchNamesLinkFieldsPerResource()
    {
        Assert.Contains("urls and character_builder_urls", SystemsHelp("metadata-fetch", full: false));
        var booksOutput = BooksHelp("metadata-fetch", full: false);
        Assert.Contains("incoming for urls is", booksOutput);
        Assert.DoesNotContain("character_builder_urls", booksOutput);
    }

    // A mistyped --source-id is a 400, same as fetch's; search's Notes must
    // say so too.
    [Fact]
    public void SearchNamesThe400Case()
    {
        Assert.Contains("400", SystemsHelp("metadata-search", full: false));
    }

    [Fact]
    public void SearchRendersItsResponseShape()
    {
        var output = SystemsHelp("metadata-search", full: true);
        Assert.Contains("\"identity\":", output);
        Assert.Contains("\"score\":", output);
    }

    [Fact]
    public void FetchRendersItsResponseShape()
    {
        var output = SystemsHelp("metadata-fetch", full: true);
        Assert.Contains("\"status\":", output);
        Assert.Contains("\"<any>\"", output);
    }

    // Neither flag is a request the server answers with anything but a 400, and
    // both is ambiguous — the server silently prefers paste.
    [Fact]
    public void FetchRefusesNeitherIdentityNorPaste()
    {
        var result = SystemsCommand.Create().Parse(["metadata-fetch", "--id", "1", "--source-id", "x"]);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Message.Contains("--identity") && e.Message.Contains("--paste"));
    }

    [Fact]
    public void FetchRefusesBothIdentityAndPaste()
    {
        var result = SystemsCommand.Create().Parse(
            ["metadata-fetch", "--id", "1", "--source-id", "x", "--identity", "a", "--paste", "b"]);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void FetchAcceptsEitherOne()
    {
        Assert.Empty(SystemsCommand.Create()
            .Parse(["metadata-fetch", "--id", "1", "--source-id", "x", "--identity", "a"]).Errors);
        Assert.Empty(SystemsCommand.Create()
            .Parse(["metadata-fetch", "--id", "1", "--source-id", "x", "--paste", "b"]).Errors);
    }

    [Fact]
    public void SearchRequiresASourceId()
    {
        var result = SystemsCommand.Create().Parse(["metadata-search", "--id", "1"]);
        Assert.NotEmpty(result.Errors);
    }
}
