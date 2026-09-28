using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class SidecarsCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(SidecarsCommand.Create(), path, full);

    [Fact]
    public void SettingsGetParsesWithNoArguments()
    {
        Assert.Empty(SidecarsCommand.Create().Parse(["settings", "get"]).Errors);
    }

    // All three routes are require_admin, so all three carry the tag.
    [Fact]
    public void SettingsGetDeclaresTheAdminRole()
    {
        Assert.Contains("Role required:\n  admin\n", Help(["sidecars", "settings", "get"]));
    }

    [Fact]
    public void SettingsGetTakesNoOptions()
    {
        Assert.NotEmpty(
            SidecarsCommand.Create().Parse(["settings", "get", "--formats", "opf"]).Errors);
    }

    [Fact]
    public void SettingsSetDeclaresTheAdminRole()
    {
        Assert.Contains("Role required:\n  admin\n", Help(["sidecars", "settings", "set"]));
    }

    // The PUT sends formats unconditionally, so a call without it would send an
    // empty list and switch the whole feature off.
    [Fact]
    public void SettingsSetRequiresFormats()
    {
        Assert.NotEmpty(SidecarsCommand.Create().Parse(["settings", "set", "--covers"]).Errors);
    }

    [Fact]
    public void SettingsSetRejectsAnUnknownFormat()
    {
        Assert.NotEmpty(
            SidecarsCommand.Create().Parse(["settings", "set", "--formats", "epub"]).Errors);
    }

    [Theory]
    [InlineData("opf")]
    [InlineData("nfo")]
    [InlineData("json")]
    [InlineData("yaml")]
    public void SettingsSetAcceptsEachFormat(string format)
    {
        Assert.Empty(
            SidecarsCommand.Create().Parse(["settings", "set", "--formats", format]).Errors);
    }

    [Fact]
    public void SettingsSetAcceptsSeveralFormatsAndBothFlags()
    {
        var parsed = SidecarsCommand.Create().Parse(
            ["settings", "set", "--formats", "opf", "json", "--covers", "--overwrite-foreign"]);
        Assert.Empty(parsed.Errors);
    }

    // The sibling command backups settings set documents the opposite semantics
    // for its own PUT, so a reader arriving from there will assume wrong.
    [Fact]
    public void SettingsSetSaysItReplacesTheWholeObject()
    {
        var output = Help(["sidecars", "settings", "set"]);
        Assert.Contains("Replaces the whole settings object", output);
        Assert.Contains("set false", output);
    }

    [Fact]
    public void ExportDeclaresTheAdminRole()
    {
        Assert.Contains("Role required:\n  admin\n", Help(["sidecars", "export"]));
    }

    [Fact]
    public void ExportTakesNoOptions()
    {
        Assert.NotEmpty(SidecarsCommand.Create().Parse(["export", "--formats", "opf"]).Errors);
    }

    // The backfill is additive and the route cannot override that, so a caller
    // expecting it to refresh stale sidecars would be wrong.
    [Fact]
    public void ExportSaysItNeverRewritesAnExistingSidecar()
    {
        var output = Help(["sidecars", "export"]);
        Assert.Contains("only the sidecars that are missing", output);
        Assert.Contains("never rewrites", output);
    }

    // Answers "do I re-run this after a metadata sweep?" — no.
    [Fact]
    public void ExportSaysEditsAndNewBooksMaintainThemselves()
    {
        Assert.Contains("refreshes a book's existing sidecars on its own", Help(["sidecars", "export"]));
    }

    [Fact]
    public void ExportDocumentsItsRefusalsAndScope()
    {
        var output = Help(["sidecars", "export"]);
        Assert.Contains("400 until a format is enabled", output);
        Assert.Contains("409 while a library scan is running", output);
        Assert.Contains("Books only", output);
        Assert.Contains("read_only", output);
        Assert.Contains("Exit 3", output);
    }
}
