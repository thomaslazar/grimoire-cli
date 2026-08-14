using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class LibraryCommandTests
{
    private static string RenderHelp(string[] path, bool full) => HelpRenderer.Render(LibraryCommand.Create(), path, full);

    [Fact]
    public void EveryCommandCarriesTheAdminTag()
    {
        foreach (var verb in new[] { "rescan", "scan-status", "cancel-scan", "cleanup-missing" })
        {
            var output = RenderHelp(["library", verb], full: false);
            Assert.Contains("Role required:", output);
            Assert.Contains("Role required:\n  admin\n", output);
        }
    }

    // The body is composed from flags, so a request shape would document a body
    // the caller never writes.
    [Fact]
    public void RescanRegistersNoRequestShape()
    {
        var output = RenderHelp(["library", "rescan"], full: true);
        Assert.DoesNotContain("Request shape:", output);
        Assert.Contains("--scope", output);
        Assert.Contains("--metadata-mode", output);
    }

    [Fact]
    public void RescanExplainsWhereAScopePathComesFrom()
    {
        var output = RenderHelp(["library", "rescan"], full: false);
        Assert.Contains("relative_path", output);
        Assert.Contains("already_running", output);
    }

    // A ChoiceOption renders its own value set, so the description must not.
    [Fact]
    public void MetadataModeListsItsValuesOnce()
    {
        var output = RenderHelp(["library", "rescan"], full: false);
        Assert.Equal(1, output.Split("missing").Length - 1);
    }

    [Fact]
    public void ScanStatusWarnsAboutTheLooseFileTrap()
    {
        var output = RenderHelp(["library", "scan-status"], full: true);
        Assert.Contains("never becomes true", output);
        Assert.Contains("Response shape:", output);
        Assert.Contains("\"running\":", output);
    }

    [Fact]
    public void CancelScanSaysItExitsZeroEitherWay()
    {
        Assert.Contains("whether or not one was running",
            RenderHelp(["library", "cancel-scan"], full: false));
    }

    // The two facts this command exists to warn about. A help block that loses
    // either has lost the point of the command.
    [Fact]
    public void CleanupMissingWarnsAboutBookmarksAndAbsentMounts()
    {
        var output = RenderHelp(["library", "cleanup-missing"], full: false);
        Assert.Contains("bookmarks", output);
        Assert.Contains("absent rather than hung", output);
    }

    [Fact]
    public void CleanupMissingSaysItLeavesFilesAlone()
    {
        Assert.Contains("Never", RenderHelp(["library", "cleanup-missing"], full: false));
        Assert.Contains("touches files", RenderHelp(["library", "cleanup-missing"], full: false));
    }

    [Fact]
    public void CleanupMissingNamesTheScanConflict()
    {
        Assert.Contains("409", RenderHelp(["library", "cleanup-missing"], full: false));
    }

    [Fact]
    public void CleanupMissingRendersItsCounts()
    {
        var output = RenderHelp(["library", "cleanup-missing"], full: true);
        Assert.Contains("\"removed\":", output);
        Assert.Contains("\"systems\":", output);
    }

    // No prompt and no --yes: this CLI's callers are agents, so the warning is
    // help text and nothing else.
    [Fact]
    public void CleanupMissingTakesNoConfirmationFlag()
    {
        var output = RenderHelp(["library", "cleanup-missing"], full: true);
        Assert.DoesNotContain("--yes", output);
        Assert.DoesNotContain("--force", output);
    }
}
