using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class FolderAccessCommandTests
{
    private static string Help(string leaf) =>
        HelpRenderer.Render(FilesCommand.Create(), ["files", "folder", "access", leaf], full: false);

    [Theory]
    [InlineData("list")]
    [InlineData("set")]
    [InlineData("remove")]
    public void AccessSitsUnderFilesFolderAndIsAdminOnly(string leaf)
    {
        var help = Help(leaf);
        Assert.Matches(@"Role required:\s*\n?\s*admin", help);
    }

    [Fact]
    public void SetTakesOnlyGmOrAdmin()
    {
        var files = FilesCommand.Create();
        Assert.Empty(files.Parse(["folder", "access", "set", "--path", "books/x", "--level", "gm"]).Errors);
        Assert.Empty(files.Parse(["folder", "access", "set", "--path", "books/x", "--level", "admin"]).Errors);
        Assert.NotEmpty(files.Parse(["folder", "access", "set", "--path", "books/x", "--level", "player"]).Errors);
        Assert.NotEmpty(files.Parse(["folder", "access", "set", "--path", "books/x"]).Errors);
    }

    [Fact]
    public void RemoveRequiresPath()
        => Assert.NotEmpty(FilesCommand.Create().Parse(["folder", "access", "remove"]).Errors);

    // Lifting the rule does not restore the shares it demoted; that is the
    // irreversible part a caller has to know before set.
    [Fact]
    public void SetWarnsThatRemoveDoesNotRestoreShares()
        => Assert.Contains("remove does not restore", Help("set").Replace("\n", " ").Replace("  ", " "));
}
