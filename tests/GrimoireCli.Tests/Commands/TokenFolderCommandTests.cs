using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class TokenFolderCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(TokensCommand.Create(), path, full);

    [Fact]
    public void FoldersListParsesWithNoArguments()
    {
        Assert.Empty(TokensCommand.Create().Parse(["folders", "list"]).Errors);
    }

    [Fact]
    public void FoldersListDeclaresNoRole()
    {
        Assert.DoesNotContain("Role required:", Help(["tokens", "folders", "list"]));
    }

    [Theory]
    [InlineData("set")]
    [InlineData("batch-set")]
    public void FolderWritesDeclareTheGmOrAdminRole(string sub)
    {
        Assert.Contains("Role required:\n  gm or admin\n", Help(["tokens", "folders", sub]));
    }

    // The endpoint is keyed by the path in the body, not a parent id.
    [Fact]
    public void FoldersSetTakesNoId()
    {
        Assert.DoesNotContain("--id", Help(["tokens", "folders", "set"]));
    }

    [Fact]
    public void FoldersSetRequiresExactlyOneBodySource()
    {
        Assert.NotEmpty(TokensCommand.Create().Parse(["folders", "set"]).Errors);
        Assert.NotEmpty(TokensCommand.Create()
            .Parse(["folders", "set", "--stdin", "--input", "f.json"]).Errors);
    }

    // Diffing a write against a read otherwise looks like data loss.
    [Fact]
    public void FoldersListNotesTheDisplayCasingAsymmetry()
    {
        Assert.Contains("display", Help(["tokens", "folders", "list"]));
    }

    // A typo'd path creates a row nothing can reach afterwards.
    [Fact]
    public void FoldersSetWarnsThatARowIsPermanent()
    {
        Assert.Contains("permanent", Help(["tokens", "folders", "set"]));
    }

    // frame_folders is on-disk state, and this is the only command that reads it
    // back — a caller told "never what is on disk" would not look here.
    [Fact]
    public void FoldersListExplainsFrameFolders()
    {
        var help = Help(["tokens", "folders", "list"]);
        Assert.Contains("frame_folders", help);
        Assert.Contains(".frames-container", help);
    }

    [Fact]
    public void FoldersSetRendersItsRequestShape()
    {
        Assert.Contains("\"path\"", Help(["tokens", "folders", "set"], full: true));
    }
}
