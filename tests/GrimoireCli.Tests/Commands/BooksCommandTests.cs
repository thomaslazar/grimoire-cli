using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class BooksCommandTests
{
    private static string RenderHelp(string[] path, bool full) => HelpRenderer.Render(BooksCommand.Create(), path, full);

    [Fact]
    public void ListDocumentsThePagingDefaultAndCap()
    {
        var output = RenderHelp(["books", "list"], full: false);
        Assert.Contains("--limit", output);
        Assert.Contains("default 100, max 500", output);
        Assert.Contains("--offset", output);
    }

    [Fact]
    public void ListShowsTheEnvelopeNotABareArray()
    {
        var output = RenderHelp(["books", "list"], full: true);
        Assert.Contains("Response shape:", output);
        Assert.Contains("\"total\":", output);
        Assert.Contains("\"books\":", output);
    }

    [Fact]
    public void GetShowsTheDetailShapeWithItsNestedSystem()
    {
        var output = RenderHelp(["books", "get"], full: true);
        Assert.Contains("\"game_system\":", output);
        Assert.Contains("\"authors\":", output);
    }

    // Both reads are guarded by require_not_guest or nothing at all, which per
    // CLAUDE.md is the default and carries no tag.
    [Fact]
    public void ReadsCarryNoRoleTag()
    {
        Assert.DoesNotContain("Role required:", RenderHelp(["books", "list"], full: false));
        Assert.DoesNotContain("Role required:", RenderHelp(["books", "get"], full: false));
    }

    [Fact]
    public void WritesCarryTheGmOrAdminTag()
    {
        foreach (var verb in new[] { "update", "batch-update", "batch-tag" })
            Assert.Contains("gm or admin", RenderHelp(["books", verb], full: false));
    }

    [Fact]
    public void UpdateShowsItsRequestShapeAndTheClearingRule()
    {
        var output = RenderHelp(["books", "update"], full: true);
        Assert.Contains("Request shape:", output);
        Assert.Contains("\"title\":", output);
        Assert.Contains("year, month and day cannot be cleared", output);
    }

    // The bulk body is an envelope, and the sample is the model
    // JsonBodyInput.Validate parses against.
    [Fact]
    public void BatchUpdateShowsTheItemsEnvelope()
    {
        var output = RenderHelp(["books", "batch-update"], full: true);
        Assert.Contains("\"items\":", output);
        Assert.Contains("Each item requires id", output);
    }

    [Fact]
    public void BatchTagShowsTheSharedIdsAndTagsBody()
    {
        var output = RenderHelp(["books", "batch-tag"], full: true);
        Assert.Contains("\"ids\":", output);
        Assert.Contains("\"tags\":", output);
    }

    [Fact]
    public void MaintenanceCommandsCarryTheGmOrAdminTag()
    {
        foreach (var verb in new[] { "reindex", "rescan" })
            Assert.Contains("gm or admin", RenderHelp(["books", verb], full: false));
    }

    // The DPI range belongs on the flag, so the Notes must not repeat it.
    [Fact]
    public void ReindexStatesItsDpiRangeOnceOnTheFlag()
    {
        var output = RenderHelp(["books", "reindex"], full: false);
        Assert.Contains("72-600", output);
        Assert.Equal(1, output.Split("72-600").Length - 1);
    }

    [Fact]
    public void ReindexSaysItIsOcrOnlyAndPointsAtScanStatus()
    {
        var output = RenderHelp(["books", "reindex"], full: false);
        Assert.Contains("OCR only", output);
        Assert.Contains("library scan-status", output);
    }

    [Fact]
    public void RescanWarnsThatALibraryScanAbsorbsIt()
    {
        var output = RenderHelp(["books", "rescan"], full: false);
        Assert.Contains("rescan_queued either way", output);
    }

    // Status-only responses name their values in Notes instead of registering a
    // shape, which would render them as "<string>".
    [Fact]
    public void MaintenanceCommandsRegisterNoResponseShape()
    {
        foreach (var verb in new[] { "reindex", "rescan" })
            Assert.DoesNotContain("Response shape:", RenderHelp(["books", verb], full: true));
    }
}
