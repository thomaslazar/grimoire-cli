using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class ModelFolderCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(ModelsCommand.Create(), path, full);

    [Fact]
    public void FoldersListParsesWithNoArguments()
    {
        Assert.Empty(ModelsCommand.Create().Parse(["folders", "list"]).Errors);
    }

    [Fact]
    public void FoldersListDeclaresNoRole()
    {
        Assert.DoesNotContain("Role required:", Help(["models", "folders", "list"]));
    }

    [Theory]
    [InlineData("set")]
    [InlineData("batch-set")]
    public void FolderWritesDeclareTheGmOrAdminRole(string sub)
    {
        Assert.Contains("Role required:\n  gm or admin\n", Help(["models", "folders", sub]));
    }

    // The endpoint is keyed by the path in the body, not a parent id.
    [Fact]
    public void FoldersSetTakesNoId()
    {
        Assert.DoesNotContain("--id", Help(["models", "folders", "set"]));
    }

    [Fact]
    public void FoldersSetRequiresExactlyOneBodySource()
    {
        Assert.NotEmpty(ModelsCommand.Create().Parse(["folders", "set"]).Errors);
        Assert.NotEmpty(ModelsCommand.Create()
            .Parse(["folders", "set", "--stdin", "--input", "f.json"]).Errors);
    }

    // Diffing a write against a read otherwise looks like data loss.
    [Fact]
    public void FoldersListNotesTheDisplayCasingAsymmetry()
    {
        Assert.Contains("display", Help(["models", "folders", "list"]));
    }

    // A typo'd path creates a row nothing can reach afterwards.
    [Fact]
    public void FoldersSetWarnsThatARowIsPermanent()
    {
        Assert.Contains("permanent", Help(["models", "folders", "set"]));
    }

    [Fact]
    public void FoldersSetRendersItsRequestShape()
    {
        Assert.Contains("\"path\"", Help(["models", "folders", "set"], full: true));
    }
}
