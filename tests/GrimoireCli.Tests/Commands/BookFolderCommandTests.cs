using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class BookFolderCommandTests
{
    private static string Help(string leaf, bool full) =>
        HelpRenderer.Render(SystemsCommand.Create(), ["systems", "book-folders", leaf], full);

    [Theory]
    [InlineData("list")]
    [InlineData("set")]
    public void EveryBookFolderVerbExists(string leaf) => Assert.Contains(leaf, Help(leaf, full: false));

    [Fact]
    public void SetIsGmOrAdmin() => Assert.Contains("gm or admin", Help("set", full: false));

    [Fact]
    public void ListCarriesNoRoleTag() =>
        Assert.DoesNotContain("Role required:", Help("list", full: false));

    [Fact]
    public void SetRequiresExactlyOneBodySource() =>
        Assert.NotEmpty(SystemsCommand.Create().Parse(["book-folders", "set", "--id", "1"]).Errors);

    [Fact]
    public void IdIsRequiredOnListAndSet()
    {
        Assert.NotEmpty(SystemsCommand.Create().Parse(["book-folders", "list"]).Errors);
        Assert.NotEmpty(SystemsCommand.Create().Parse(["book-folders", "set", "--stdin"]).Errors);
    }

    [Fact]
    public void SetWarnsThatItReplacesTags()
    {
        Assert.Contains("Replaces the folder's tag list", Help("set", full: false));
    }

    [Fact]
    public void SetWarnsThatTheIdIsIgnored()
    {
        Assert.Contains("ignores the --id", Help("set", full: false));
    }

    [Fact]
    public void SetWarnsThatTagsEchoAsInternalKeys()
    {
        Assert.Contains("internal keys", Help("set", full: false));
    }

    [Fact]
    public void ListExplainsThatFolderTagsNeverAppearOnBooks()
    {
        Assert.Contains("never appear", Help("list", full: false));
        Assert.Contains("in a book's own tags", Help("list", full: false));
    }
}
