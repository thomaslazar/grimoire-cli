using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

/// <summary>
/// The archive command's help is the only place the eleven scopes and their
/// required flags are written down, so the tests treat that table as part of
/// the interface rather than as prose.
/// </summary>
public class DownloadsCommandTests
{
    private static string Help(bool full = false) =>
        HelpRenderer.Render(DownloadsCommand.Create(), ["downloads", "archive"], full);

    [Fact]
    public void TheGroupHostsArchive()
    {
        Assert.Equal(["archive"], DownloadsCommand.Create().Subcommands.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void ArchiveRequiresATypeAndAnOutput()
    {
        var downloads = DownloadsCommand.Create();
        Assert.NotEmpty(downloads.Parse(["archive", "--output", "a.zip"]).Errors);
        Assert.NotEmpty(downloads.Parse(["archive", "--type", "system"]).Errors);
        Assert.Empty(downloads.Parse(["archive", "--type", "system", "--id", "s1", "--output", "a.zip"]).Errors);
    }

    // Every scope flag is optional at parse time: which ones a type needs is
    // the server's rule, and it answers 400 naming the missing one.
    [Fact]
    public void EveryScopeFlagParsesAndNoneIsValidatedClientSide()
    {
        Assert.Empty(DownloadsCommand.Create().Parse([
            "archive", "--type", "tag_folder", "--fmt", "tar.gz", "--id", "s1",
            "--category", "core", "--tag", "session-prep", "--resource-type", "book",
            "--folder", "errata", "--output", "-"]).Errors);
        // A type the server will reject still parses — no client-side set.
        Assert.Empty(DownloadsCommand.Create().Parse(
            ["archive", "--type", "sausage", "--output", "-"]).Errors);
        Assert.Empty(DownloadsCommand.Create().Parse(
            ["archive", "--type", "system", "--fmt", "sausage", "--output", "-"]).Errors);
    }

    [Theory]
    [InlineData("system")]
    [InlineData("system_category")]
    [InlineData("book_folder")]
    [InlineData("map_folder")]
    [InlineData("token_folder")]
    [InlineData("audio_folder")]
    [InlineData("model_folder")]
    [InlineData("library_folder")]
    [InlineData("tag")]
    [InlineData("tag_type")]
    [InlineData("tag_folder")]
    public void TheHelpNamesEveryScope(string scope)
    {
        Assert.Contains(scope, Help());
    }

    [Fact]
    public void TheHelpNamesEveryFormat()
    {
        var help = Help();
        foreach (var fmt in new[] { "zip", "tar", "tar.gz", "tar.bz2" })
            Assert.Contains(fmt, help);
    }

    // library_folder is the one scope with a role rule, enforced inside the
    // handler rather than on the route, so the command carries no role tag.
    [Fact]
    public void TheHelpFlagsTheAdminOnlyScopeButTheCommandHasNoRole()
    {
        Assert.Contains("admin", Help());
        Assert.DoesNotContain("Role required:", Help(full: true));
    }

    [Fact]
    public void ArchiveCarriesTheSavedFileShape()
    {
        Assert.Contains("Response shape:", Help(full: true));
    }
}
