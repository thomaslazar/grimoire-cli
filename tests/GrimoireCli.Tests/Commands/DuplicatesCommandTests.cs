using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class DuplicatesCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(DuplicatesCommand.Create(), path, full);

    [Theory]
    [InlineData("link")]
    [InlineData("promote")]
    [InlineData("unlink")]
    [InlineData("merge-metadata")]
    [InlineData("delete")]
    [InlineData("compare")]
    public void EveryResolutionCommandDeclaresTheAdminRole(string leaf)
    {
        var output = Help(["duplicates", leaf]);
        Assert.Contains("Role required:", output);
        Assert.Contains("admin", output);
    }

    [Theory]
    [InlineData("link")]
    [InlineData("promote")]
    [InlineData("unlink")]
    [InlineData("merge-metadata")]
    [InlineData("delete")]
    [InlineData("compare")]
    public void EveryResolutionCommandCarriesAResponseShape(string leaf)
    {
        Assert.Contains("Response shape:", Help(["duplicates", leaf], full: true));
    }

    [Fact]
    public void LinkRequiresExactlyOneBodySource()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(["link"]).Errors);
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(["link", "--stdin", "--input", "a.json"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(["link", "--stdin"]).Errors);
    }

    // The generated models type resource_type as an enum, so the model cannot
    // hold anything else — the allowed set comes from the generator, not from a
    // hand-written mirror of server policy.
    [Theory]
    [InlineData("book")]
    [InlineData("map")]
    [InlineData("token")]
    [InlineData("audio")]
    public void CompareAcceptsEveryResourceType(string type)
    {
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["compare", "--resource-type", type, "--ids", "a", "b"]).Errors);
    }

    [Fact]
    public void CompareRejectsAnUnknownResourceType()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(
            ["compare", "--resource-type", "spellbook", "--ids", "a", "b"]).Errors);
    }

    [Fact]
    public void CompareTakesRepeatableIds()
    {
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["compare", "--resource-type", "book", "--ids", "a", "--ids", "b"]).Errors);
    }

    // With neither flag the server answers 200 {"unlinked": []} — a silent
    // no-op, so the refusal has to happen here.
    [Fact]
    public void UnlinkRequiresIdsOrAParent()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(
            ["unlink", "--resource-type", "book"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["unlink", "--resource-type", "book", "--ids", "a"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["unlink", "--resource-type", "book", "--parent-id", "p"]).Errors);
    }

    // The server defaults delete_file to true and a bodyless call destroys the
    // file, while `files delete` spells the same flag and defaults to soft.
    // Requiring the value is what stops the two verbs being confused.
    [Fact]
    public void DeleteRequiresTheDeleteFileValue()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(
            ["delete", "--resource-type", "book", "--id", "a"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["delete", "--resource-type", "book", "--id", "a", "--delete-file", "false"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["delete", "--resource-type", "book", "--id", "a", "--delete-file", "true"]).Errors);
    }

    [Fact]
    public void DeleteTakesAnEmptyReparentTo()
    {
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["delete", "--resource-type", "book", "--id", "a", "--delete-file", "true", "--reparent-to", ""]).Errors);
    }

    [Fact]
    public void PromoteRequiresBothParents()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(
            ["promote", "--resource-type", "book", "--new-parent-id", "n"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["promote", "--resource-type", "book", "--new-parent-id", "n", "--old-parent-id", "o"]).Errors);
    }

    [Fact]
    public void MergeMetadataRequiresSourceTargetAndFields()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(
            ["merge-metadata", "--resource-type", "book", "--source-id", "s", "--target-id", "t"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["merge-metadata", "--resource-type", "book", "--source-id", "s", "--target-id", "t", "--fields", "title"]).Errors);
    }

    // Nothing exposes the copyable set: /compare declares response_model
    // CompareResult, which omits mergeable_fields.
    [Fact]
    public void MergeMetadataListsTheBookFields()
    {
        var output = Help(["duplicates", "merge-metadata"]);
        Assert.Contains("publisher_url", output);
        Assert.Contains("is_explicit", output);
    }

    // The kind vocabulary is closed and scoped by collection, and the server
    // reports a bad one per child rather than failing the request.
    [Fact]
    public void LinkListsTheScopedKindVocabulary()
    {
        var output = Help(["duplicates", "link"]);
        Assert.Contains("form-fillable", output);
        Assert.Contains("universal-vtt", output);
        Assert.Contains("color-variation", output);
        Assert.Contains("sped-up", output);
    }

    [Fact]
    public void DeleteDocumentsWhatItRemovesAndTheReparentRule()
    {
        var output = Help(["duplicates", "delete"]);
        Assert.Contains("bookmarks", output);
        Assert.Contains("--reparent-to", output);
        Assert.Contains("409", output);
    }
}
