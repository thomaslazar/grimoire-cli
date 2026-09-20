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

    // serve_map_thumbnail depends on get_current_user, which carries no tag.
    [Fact]
    public void ThumbnailDeclaresNoRole()
    {
        Assert.DoesNotContain("Role required:", Help(["maps", "thumbnail"]));
    }

    [Fact]
    public void ThumbnailRequiresAnIdAndAnOutput()
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["thumbnail", "--id", "x"]).Errors);
        Assert.NotEmpty(MapsCommand.Create().Parse(["thumbnail", "--output", "x.webp"]).Errors);
        Assert.Empty(MapsCommand.Create().Parse(["thumbnail", "--id", "x", "--output", "x.webp"]).Errors);
    }

    [Fact]
    public void TheGroupHostsTheNewGetters()
    {
        var names = MapsCommand.Create().Subcommands.Select(c => c.Name).ToArray();
        Assert.Contains("file", names);
        Assert.Contains("page", names);
        Assert.Contains("vtt", names);
    }

    [Fact]
    public void FileAndPageRequireAnOutput()
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["file", "--id", "m1"]).Errors);
        Assert.Empty(MapsCommand.Create().Parse(["file", "--id", "m1", "--output", "m.png"]).Errors);
        Assert.NotEmpty(MapsCommand.Create().Parse(["page", "--id", "m1", "--page", "1"]).Errors);
        Assert.Empty(MapsCommand.Create().Parse(["page", "--id", "m1", "--page", "1", "--output", "-"]).Errors);
    }

    [Fact]
    public void PageRequiresAPageNumberAndTakesAWidth()
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["page", "--id", "m1", "--output", "-"]).Errors);
        Assert.Empty(MapsCommand.Create().Parse(
            ["page", "--id", "m1", "--page", "2", "--width", "800", "--output", "-"]).Errors);
    }

    // An image map accepts page 1 only, and --width has a server-side ceiling;
    // both are things a caller cannot read off the flags.
    [Fact]
    public void PageDocumentsTheImageMapAndWidthLimits()
    {
        var help = Help(["maps", "page"]);
        Assert.Contains("page 1", help);
        Assert.Contains("3000", help);
    }

    [Theory]
    [InlineData("image")]
    [InlineData("data")]
    [InlineData("export")]
    public void TheVttSubgroupHostsItsThreeVerbs(string leaf)
    {
        var vtt = MapsCommand.Create().Subcommands.Single(c => c.Name == "vtt");
        Assert.Contains(leaf, vtt.Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void VttImageAndExportStreamButDataDoesNot()
    {
        var maps = MapsCommand.Create();
        Assert.Empty(maps.Parse(["vtt", "image", "--id", "m1", "--output", "-"]).Errors);
        Assert.Empty(maps.Parse(["vtt", "export", "--id", "m1", "--output", "-"]).Errors);
        Assert.Empty(maps.Parse(["vtt", "data", "--id", "m1"]).Errors);
        // data is JSON on stdout; an --output flag would imply a file it never writes.
        Assert.NotEmpty(maps.Parse(["vtt", "data", "--id", "m1", "--output", "-"]).Errors);
    }

    // Both refuse a map that is not a Universal VTT, which is most maps.
    [Theory]
    [InlineData("image")]
    [InlineData("data")]
    public void TheVttGettersWarnTheyNeedAUniversalVttMap(string leaf)
    {
        var help = Help(["maps", "vtt", leaf]);
        Assert.Contains("400", help);
    }

    [Fact]
    public void VttExportDocumentsWhatItRefuses()
    {
        var help = Help(["maps", "vtt", "export"]);
        Assert.Contains("PDF", help);
    }

    [Theory]
    [InlineData(new object[] { new[] { "maps", "file" } })]
    [InlineData(new object[] { new[] { "maps", "page" } })]
    [InlineData(new object[] { new[] { "maps", "vtt", "image" } })]
    [InlineData(new object[] { new[] { "maps", "vtt", "export" } })]
    public void EveryStreamingGetterCarriesTheSavedFileShape(string[] path)
    {
        Assert.Contains("Response shape:", Help(path, full: true));
    }

    [Theory]
    [InlineData(new object[] { new[] { "maps", "file" } })]
    [InlineData(new object[] { new[] { "maps", "page" } })]
    [InlineData(new object[] { new[] { "maps", "vtt", "image" } })]
    [InlineData(new object[] { new[] { "maps", "vtt", "data" } })]
    [InlineData(new object[] { new[] { "maps", "vtt", "export" } })]
    public void NoNewMapGetterDeclaresARole(string[] path)
    {
        Assert.DoesNotContain("Role required:", Help(path, full: true));
    }
}
