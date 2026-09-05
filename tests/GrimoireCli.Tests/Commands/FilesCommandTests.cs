using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class FilesCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(FilesCommand.Create(), path, full);

    [Theory]
    [InlineData("browse")]
    [InlineData("upload")]
    public void EveryCommandDeclaresTheAdminRole(string leaf)
    {
        var output = Help(["files", leaf]);
        Assert.Contains("Role required:", output);
        Assert.Contains("admin", output);
    }

    [Theory]
    [InlineData("browse")]
    [InlineData("upload")]
    public void EveryCommandCarriesAResponseShape(string leaf)
    {
        Assert.Contains("Response shape:", Help(["files", leaf], full: true));
    }

    // --path is optional: the server lists the library root for an empty path.
    [Fact]
    public void BrowseParsesWithNoArguments()
    {
        Assert.Empty(FilesCommand.Create().Parse(["browse"]).Errors);
    }

    // The server clamps limit to max(1, min(limit, 2000)) and answers 200, so a
    // value outside the range would be silently honoured as a different one.
    [Theory]
    [InlineData("0")]
    [InlineData("2001")]
    [InlineData("-5")]
    public void BrowseRejectsALimitOutsideTheServersRange(string limit)
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(["browse", "--limit", limit]).Errors);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2000")]
    public void BrowseAcceptsTheBoundsOfTheServersRange(string limit)
    {
        Assert.Empty(FilesCommand.Create().Parse(["browse", "--limit", limit]).Errors);
    }

    [Fact]
    public void UploadRequiresDestinationAndFile()
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(["upload", "--destination", "books"]).Errors);
        Assert.NotEmpty(FilesCommand.Create().Parse(["upload", "--file", "a.pdf"]).Errors);
        Assert.Empty(FilesCommand.Create().Parse(["upload", "--destination", "books", "--file", "a.pdf"]).Errors);
    }

    // upload's on_conflict is an unvalidated Form field upstream, and _dest_for
    // treats anything that is not "skip" as rename — so an unknown value would
    // silently rename and answer 200.
    [Fact]
    public void UploadRejectsAnUnknownConflictPolicy()
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(
            ["upload", "--destination", "books", "--file", "a.pdf", "--on-conflict", "overwrite"]).Errors);
    }

    [Theory]
    [InlineData("skip")]
    [InlineData("rename")]
    public void UploadAcceptsTheTwoConflictPolicies(string policy)
    {
        Assert.Empty(FilesCommand.Create().Parse(
            ["upload", "--destination", "books", "--file", "a.pdf", "--on-conflict", policy]).Errors);
    }

    [Fact]
    public void UploadDocumentsThatItTakesOneFilePerRequest()
    {
        var output = Help(["files", "upload"]);
        Assert.Contains("one file", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrowseDocumentsItsCapAndTheIndexedDistinction()
    {
        var output = Help(["files", "browse"]);
        Assert.Contains("truncated", output);
        Assert.Contains("record_id", output);
    }

    // Reproduces the unguarded File.ReadAllBytesAsync: a missing --file must
    // report BodyInputException rather than throw raw, before any client call.
    [Fact]
    public async Task UploadReportsAMissingFileInsteadOfThrowingRaw()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"upload-missing-{Guid.NewGuid():N}.pdf");
        var service = new GrimoireCli.Services.FilesService(null!);
        var ex = await Assert.ThrowsAsync<BodyInputException>(
            () => service.UploadAsync("books", missing, null, null));
        Assert.Contains("Could not read", ex.Message);
        Assert.Contains(missing, ex.Message);
    }

    [Theory]
    [InlineData("move")]
    [InlineData("rename")]
    [InlineData("delete")]
    public void TheMutatingCommandsDeclareTheAdminRole(string leaf)
    {
        var output = Help(["files", leaf]);
        Assert.Contains("Role required:", output);
        Assert.Contains("admin", output);
    }

    [Fact]
    public void MoveTakesRepeatableSourcesAndRequiresADestination()
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(["move", "--sources", "a"]).Errors);
        Assert.Empty(FilesCommand.Create().Parse(
            ["move", "--sources", "a", "b", "--destination", "books"]).Errors);
        Assert.Empty(FilesCommand.Create().Parse(
            ["move", "--sources", "a", "--sources", "b", "--destination", "books"]).Errors);
    }

    [Fact]
    public void RenameRequiresPathAndNewName()
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(["rename", "--path", "a"]).Errors);
        Assert.Empty(FilesCommand.Create().Parse(["rename", "--path", "a", "--new-name", "b"]).Errors);
    }

    [Fact]
    public void DeleteRequiresAPathAndDefaultsToTheSoftForm()
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(["delete"]).Errors);
        Assert.Empty(FilesCommand.Create().Parse(["delete", "--path", "a"]).Errors);
        Assert.Empty(FilesCommand.Create().Parse(["delete", "--path", "a", "--delete-files"]).Errors);
    }

    // The two deletes behave oppositely and nothing in their names says so, so
    // each must state its own default where an agent will read it.
    [Fact]
    public void DeleteDocumentsThatItIsSoftUnlessAsked()
    {
        var output = Help(["files", "delete"]);
        Assert.Contains("--delete-files", output);
        Assert.Contains("rescan", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("428", output);
    }

    [Fact]
    public void MoveDocumentsThatItSkipsWhereUploadRenames()
    {
        var output = Help(["files", "move"]);
        Assert.Contains("skip", output, StringComparison.OrdinalIgnoreCase);
    }

    // Each delete must name the other's opposite behaviour, or an agent reading
    // only one of them assumes they match. Both directions are pinned.
    [Fact]
    public void FileDeleteNamesTheFolderDeleteContrast()
    {
        Assert.Contains("folder delete", Help(["files", "delete"]));
    }

    [Theory]
    [InlineData("create")]
    [InlineData("delete")]
    [InlineData("markers")]
    [InlineData("scaffold")]
    [InlineData("contents")]
    public void EveryFolderCommandDeclaresTheAdminRole(string leaf)
    {
        var output = HelpRenderer.Render(FilesCommand.Create(), ["files", "folder", leaf], full: false);
        Assert.Contains("Role required:", output);
        Assert.Contains("admin", output);
    }

    [Fact]
    public void TheGroupHostsTheFolderSubgroup()
    {
        var folder = FilesCommand.Create().Subcommands.Single(c => c.Name == "folder");
        Assert.Equal(
            ["create", "delete", "markers", "scaffold", "contents"],
            folder.Subcommands.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void FolderCreateRequiresParentAndName()
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(["folder", "create", "--parent", "books"]).Errors);
        Assert.Empty(FilesCommand.Create().Parse(["folder", "create", "--parent", "books", "--name", "X"]).Errors);
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("one-page")]
    [InlineData("agnostic")]
    [InlineData("family")]
    [InlineData("publisher")]
    [InlineData("generic")]
    public void FolderCreateAcceptsEveryContainerKind(string kind)
    {
        Assert.Empty(FilesCommand.Create().Parse(
            ["folder", "create", "--parent", "books", "--name", "X", "--container-kind", kind]).Errors);
    }

    [Fact]
    public void FolderCreateRejectsAnUnknownContainerKind()
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(
            ["folder", "create", "--parent", "books", "--name", "X", "--container-kind", "shelf"]).Errors);
    }

    // The one-of-a-kind kinds are the trap: a second one is refused server-side.
    [Fact]
    public void FolderCreateDocumentsTheSingletonKinds()
    {
        var output = HelpRenderer.Render(FilesCommand.Create(), ["files", "folder", "create"], full: false);
        Assert.Contains("one-page", output);
        Assert.Contains("singletons_taken", output);
    }

    // files delete is soft by default; this one never is, and only its own help
    // can say so where an agent will read it.
    [Fact]
    public void FolderDeleteDocumentsThatItAlwaysRemovesTheFiles()
    {
        var output = HelpRenderer.Render(FilesCommand.Create(), ["files", "folder", "delete"], full: false);
        Assert.Contains("428", output);
        Assert.Contains("files delete", output);
    }

    [Fact]
    public void FolderMarkersAndContentsAndScaffoldRequireAPath()
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(["folder", "markers"]).Errors);
        Assert.NotEmpty(FilesCommand.Create().Parse(["folder", "scaffold"]).Errors);
        Assert.NotEmpty(FilesCommand.Create().Parse(["folder", "contents"]).Errors);
        Assert.Empty(FilesCommand.Create().Parse(["folder", "markers", "--path", "a"]).Errors);
    }
}
