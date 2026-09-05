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
}
