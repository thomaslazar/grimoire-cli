using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class MapsCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(MapsCommand.Create(), path, full);

    [Fact]
    public void ListParsesWithNoArguments()
    {
        Assert.Empty(MapsCommand.Create().Parse(["list"]).Errors);
    }

    // Both reads are require_not_guest or weaker, which carries no tag. A tag
    // here would claim a permission the endpoint does not ask for.
    [Theory]
    [InlineData("list")]
    [InlineData("get")]
    public void ReadsDeclareNoRole(string sub)
    {
        Assert.DoesNotContain("Role required:", Help(["maps", sub]));
    }

    // -1 is documented as "unlimited" on search. Here it means unlimited without
    // --folder and "drop the last row" with it, because the server pages by
    // Python slice in that branch. Refusing it client-side is the whole point of
    // the floor.
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void ListRejectsALimitBelowOne(string limit)
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["list", "--limit", limit]).Errors);
    }

    // The server declares no ceiling, so the CLI must not invent one.
    [Fact]
    public void ListAcceptsALimitAboveAnyServerPageSize()
    {
        Assert.Empty(MapsCommand.Create().Parse(["list", "--limit", "100000"]).Errors);
    }

    [Fact]
    public void ListRejectsANegativeOffset()
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["list", "--offset", "-1"]).Errors);
    }

    // The exact-match rule changes what a caller asks for, so it has to be said.
    [Fact]
    public void ListNotesThatFolderIsNotASubtree()
    {
        Assert.Contains("exact folder", Help(["maps", "list"]));
    }

    // Two grids in one response read as duplicates without this.
    [Fact]
    public void GetNotesTheDifferenceBetweenDetectedAndOverriddenGrid()
    {
        var help = Help(["maps", "get"]);
        Assert.Contains("detection", help);
        Assert.Contains("override", help);
    }

    [Fact]
    public void GetRequiresAnId()
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["get"]).Errors);
    }

    [Fact]
    public void ListRendersItsResponseShape()
    {
        Assert.Contains("\"maps\"", Help(["maps", "list"], full: true));
    }

    [Fact]
    public void GetRendersItsResponseShape()
    {
        Assert.Contains("\"folder_tags\"", Help(["maps", "get"], full: true));
    }
}
