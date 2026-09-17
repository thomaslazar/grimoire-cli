using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class MapFolderCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(MapsCommand.Create(), path, full);

    [Fact]
    public void FoldersListParsesWithNoArguments()
    {
        Assert.Empty(MapsCommand.Create().Parse(["folders", "list"]).Errors);
    }

    [Fact]
    public void FoldersListDeclaresNoRole()
    {
        Assert.DoesNotContain("Role required:", Help(["maps", "folders", "list"]));
    }

    [Theory]
    [InlineData("set")]
    [InlineData("batch-set")]
    public void FolderWritesDeclareTheGmOrAdminRole(string sub)
    {
        Assert.Contains("Role required:\n  gm or admin\n", Help(["maps", "folders", sub]));
    }

    // The endpoint is keyed by the path in the body. An --id here would imply a
    // parent resource this collection does not have.
    [Fact]
    public void FoldersSetTakesNoId()
    {
        Assert.DoesNotContain("--id", Help(["maps", "folders", "set"]));
    }

    [Fact]
    public void FoldersSetRequiresExactlyOneBodySource()
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["folders", "set"]).Errors);
        Assert.NotEmpty(MapsCommand.Create()
            .Parse(["folders", "set", "--stdin", "--input", "f.json"]).Errors);
    }

    // Diffing a write against a read otherwise looks like data loss.
    [Fact]
    public void FoldersListNotesTheDisplayCasingAsymmetry()
    {
        Assert.Contains("display", Help(["maps", "folders", "list"]));
    }

    [Fact]
    public void FoldersSetRendersItsRequestShape()
    {
        Assert.Contains("\"path\"", Help(["maps", "folders", "set"], full: true));
    }
}
