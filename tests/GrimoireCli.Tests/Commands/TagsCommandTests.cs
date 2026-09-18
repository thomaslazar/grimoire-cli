using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class TagsCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(TagsCommand.Create(), path, full);

    [Fact]
    public void TheGroupHostsTheReadsThenTheWrites()
    {
        Assert.Equal(
            ["list", "items", "create", "rename", "delete", "merge"],
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
    [InlineData("create")]
    [InlineData("rename")]
    [InlineData("merge")]
    public void EveryCommandCarriesAResponseShape(string leaf)
    {
        Assert.Contains("Response shape:", Help(["tags", leaf], full: true));
    }

    // 204: a registered response shape would render an empty sample and read as
    // a body the command never prints.
    [Fact]
    public void DeleteRegistersNoResponseShape()
    {
        Assert.DoesNotContain("Response shape:", Help(["tags", "delete"], full: true));
    }

    [Theory]
    [InlineData("create")]
    [InlineData("rename")]
    [InlineData("delete")]
    [InlineData("merge")]
    public void EveryWriteDeclaresTheGmOrAdminRole(string leaf)
    {
        var help = Help(["tags", leaf]);
        Assert.Contains("Role required:", help);
        Assert.Contains("gm or admin", help);
    }

    [Theory]
    [InlineData(new object[] { new[] { "create" } })]
    [InlineData(new object[] { new[] { "rename", "--tag", "a" } })]
    [InlineData(new object[] { new[] { "rename", "--display", "A" } })]
    [InlineData(new object[] { new[] { "delete" } })]
    [InlineData(new object[] { new[] { "merge", "--tag", "a" } })]
    [InlineData(new object[] { new[] { "merge", "--into", "b" } })]
    public void EveryWriteRequiresItsFlags(string[] args)
    {
        Assert.NotEmpty(TagsCommand.Create().Parse(args).Errors);
    }

    [Theory]
    [InlineData(new object[] { new[] { "create", "--value", "GM Screen" } })]
    [InlineData(new object[] { new[] { "create", "--value", "gm-screen", "--display", "GM Screen" } })]
    [InlineData(new object[] { new[] { "rename", "--tag", "a", "--display", "A" } })]
    [InlineData(new object[] { new[] { "delete", "--tag", "a" } })]
    [InlineData(new object[] { new[] { "merge", "--tag", "a", "--into", "b" } })]
    public void EveryWriteParsesWithItsFlags(string[] args)
    {
        Assert.Empty(TagsCommand.Create().Parse(args).Errors);
    }

    // The issue this group was written from claimed rename leaves the internal
    // key alone. It does not (services/tag_service/_admin.py:68), and a caller
    // who believes otherwise loses a tag to a silent merge.
    [Fact]
    public void RenameWarnsThatTheKeyFollowsAndMayMerge()
    {
        var help = Help(["tags", "rename"]);
        Assert.Contains("internal key follows", help);
        Assert.Contains("merged", help);
    }

    // Folder-derived carriers survive a merge, so the source tag reappears in
    // tags list with a count — otherwise unexplainable.
    [Fact]
    public void MergeWarnsThatFolderTagsAreLeftBehind()
    {
        Assert.Contains("folder", Help(["tags", "merge"]));
    }

    [Fact]
    public void DeleteWarnsThatItCannotBeUndone()
    {
        Assert.Contains("cannot be undone", Help(["tags", "delete"]));
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
