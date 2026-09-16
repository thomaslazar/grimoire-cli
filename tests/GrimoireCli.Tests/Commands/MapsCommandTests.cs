using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class MapsCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(MapsCommand.Create(), path, full);

    [Fact]
    public void ListParsesWithNoArguments()
    {
        Assert.Empty(MapsCommand.Create().Parse(["list"]).Errors);
    }

    // Both reads are require_not_guest or weaker, which carries no tag. A tag
    // here would claim a permission the endpoint does not ask for.
    [Theory]
    [InlineData("list")]
    [InlineData("get")]
    public void ReadsDeclareNoRole(string sub)
    {
        Assert.DoesNotContain("Role required:", Help(["maps", sub]));
    }

    // -1 is documented as "unlimited" on search. Here it means unlimited without
    // --folder and "drop the last row" with it, because the server pages by
    // Python slice in that branch. Refusing it client-side is the whole point of
    // the floor.
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void ListRejectsALimitBelowOne(string limit)
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["list", "--limit", limit]).Errors);
    }

    // The server declares no ceiling, so the CLI must not invent one.
    [Fact]
    public void ListAcceptsALimitAboveAnyServerPageSize()
    {
        Assert.Empty(MapsCommand.Create().Parse(["list", "--limit", "100000"]).Errors);
    }

    [Fact]
    public void ListRejectsANegativeOffset()
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["list", "--offset", "-1"]).Errors);
    }

    // The exact-match rule changes what a caller asks for, so it has to be said.
    [Fact]
    public void ListNotesThatFolderIsNotASubtree()
    {
        Assert.Contains("exact folder", Help(["maps", "list"]));
    }

    // Two grids in one response read as duplicates without this.
    [Fact]
    public void GetNotesTheDifferenceBetweenDetectedAndOverriddenGrid()
    {
        var help = Help(["maps", "get"]);
        Assert.Contains("detection", help);
        Assert.Contains("override", help);
    }

    [Fact]
    public void GetRequiresAnId()
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["get"]).Errors);
    }

    [Fact]
    public void ListRendersItsResponseShape()
    {
        Assert.Contains("\"maps\"", Help(["maps", "list"], full: true));
    }

    [Fact]
    public void GetRendersItsResponseShape()
    {
        Assert.Contains("\"folder_tags\"", Help(["maps", "get"], full: true));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("batch-update")]
    [InlineData("batch-tag")]
    public void WritesDeclareTheGmOrAdminRole(string sub)
    {
        Assert.Contains("Role required:\n  gm or admin\n", Help(["maps", sub]));
    }

    [Fact]
    public void UpdateRequiresExactlyOneBodySource()
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["update", "--id", "x"]).Errors);
        Assert.NotEmpty(MapsCommand.Create()
            .Parse(["update", "--id", "x", "--stdin", "--input", "f.json"]).Errors);
    }

    // The asymmetry a caller cannot guess: a 0 clears on update and is dropped
    // on batch-update, so both commands have to say so.
    [Fact]
    public void UpdateDocumentsTheGridClear()
    {
        Assert.Contains("0 to clear", Help(["maps", "update"]));
    }

    [Fact]
    public void BatchUpdateSaysItCannotClearAGrid()
    {
        Assert.Contains("cannot", Help(["maps", "batch-update"]));
    }

    // grid_warning is not a partial write; conflating it with exit 3 would tell
    // a caller a successful write failed.
    [Fact]
    public void UpdateSaysTheGridWarningIsAdvisory()
    {
        Assert.Contains("advisory", Help(["maps", "update"]));
    }

    [Fact]
    public void BatchesDocumentTheThousandItemCap()
    {
        Assert.Contains("1000", Help(["maps", "batch-update"]));
        Assert.Contains("1000", Help(["maps", "batch-tag"]));
    }

    [Fact]
    public void UpdateRendersTheRequestShape()
    {
        Assert.Contains("\"grid_px\"", Help(["maps", "update"], full: true));
    }

    // The server ignores an unknown key rather than rejecting it, so a misspelled
    // field would otherwise answer {"status": "ok"} having written nothing.
    [Fact]
    public void UpdateRejectsAFieldMapUpdateDoesNotDeclare()
    {
        var ex = Assert.Throws<BodyInputException>(() => JsonBodyInput.Validate(
            "{\"grid_pixels\":70}",
            GrimoireCli.Generated.Models.MapUpdate.CreateFromDiscriminatorValue,
            "pass it with --id"));
        Assert.Contains("grid_pixels", ex.Message);
    }

    // Harvested from books, where a validate hook makes it true. Maps has none:
    // anything but an unresolved id fails the envelope and discards the batch.
    [Fact]
    public void BatchUpdateSaysAnInvalidItemDiscardsTheWholeBatch()
    {
        var help = Help(["maps", "batch-update"]);
        Assert.Contains("Only an unresolved id lands in errors", help);
        Assert.Contains("422s the whole batch", help);
    }

    [Fact]
    public void UpdateDocumentsThatNullDoesNotClearAField()
    {
        Assert.Contains("an explicit null", Help(["maps", "update"]));
    }
}
