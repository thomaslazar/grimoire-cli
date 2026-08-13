using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class LibraryCommandTests
{
    private static string RenderHelp(string[] path, bool full) => HelpRenderer.Render(LibraryCommand.Create(), path, full);

    [Fact]
    public void AllThreeCarryTheAdminTag()
    {
        foreach (var verb in new[] { "rescan", "scan-status", "cancel-scan" })
            Assert.Contains("admin", RenderHelp(["library", verb], full: false));
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
}
