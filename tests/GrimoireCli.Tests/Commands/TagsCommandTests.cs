using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class TagsCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(TagsCommand.Create(), path, full);

    [Fact]
    public void TheGroupHostsBothReads()
    {
        Assert.Equal(
            ["list", "items"],
            TagsCommand.Create().Subcommands.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void ListParsesWithNoArguments()
    {
        Assert.Empty(TagsCommand.Create().Parse(["list"]).Errors);
    }

    [Fact]
    public void ItemsRequiresATag()
    {
        Assert.NotEmpty(TagsCommand.Create().Parse(["items"]).Errors);
        Assert.Empty(TagsCommand.Create().Parse(["items", "--tag", "dungeon"]).Errors);
    }

    // The server validates both type flags itself, answering 400 with the value
    // set, so the CLI declares no client-side set to mirror it.
    [Theory]
    [InlineData(new object[] { new[] { "list", "--in-use-by", "sausage" } })]
    [InlineData(new object[] { new[] { "items", "--tag", "a", "--resource-type", "sausage" } })]
    public void TheTypeFlagsAreNotValidatedClientSide(string[] args)
    {
        Assert.Empty(TagsCommand.Create().Parse(args).Errors);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("items")]
    public void EveryCommandCarriesAResponseShape(string leaf)
    {
        Assert.Contains("Response shape:", Help(["tags", leaf], full: true));
    }

    // Both reads are guarded by get_current_user, which carries no role.
    [Theory]
    [InlineData("list")]
    [InlineData("items")]
    public void NoCommandDeclaresARole(string leaf)
    {
        Assert.DoesNotContain("Role required:", Help(["tags", leaf]));
    }

    // A count with no shared-tag row behind it is otherwise unexplainable.
    [Fact]
    public void ListDocumentsTheMergedFolderTags()
    {
        Assert.Contains("Folder-derived tags", Help(["tags", "list"]));
    }

    // The generated sample renders the union as a bare list of type names, so
    // the five shapes have to be spelled out or the response is unreadable.
    [Theory]
    [InlineData("book")]
    [InlineData("map")]
    [InlineData("token")]
    [InlineData("audio")]
    [InlineData("system")]
    public void ItemsSpellsOutEveryItemShape(string itemType)
    {
        Assert.Contains($"item_type \"{itemType}\"", Help(["tags", "items"], full: true));
    }
}
