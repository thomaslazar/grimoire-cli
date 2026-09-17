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

    [Fact]
    public void FoldersListNotesTheDisplayCasingAsymmetry()
    {
        Assert.Contains("display", Help(["tokens", "folders", "list"]));
    }

    [Fact]
    public void FoldersSetWarnsThatARowIsPermanent()
    {
        Assert.Contains("permanent", Help(["tokens", "folders", "set"]));
    }

    [Fact]
    public void FoldersSetRendersItsRequestShape()
    {
        Assert.Contains("\"path\"", Help(["tokens", "folders", "set"], full: true));
    }
}
