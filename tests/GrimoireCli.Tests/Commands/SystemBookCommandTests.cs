using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class SystemBookCommandTests
{
    private static RootCommand Root() => new() { SystemsCommand.Create() };

    private static string Help(string[] path) =>
        HelpRenderer.Render(SystemsCommand.Create(), path, false);

    private static Command Books() =>
        SystemsCommand.Create().Subcommands.Single(c => c.Name == "books");

    [Theory]
    [InlineData("list")]
    [InlineData("folder-counts")]
    [InlineData("facets")]
    public void TheBooksSubgroupHostsTheShelfCommands(string name)
        => Assert.Contains(name, Books().Subcommands.Select(c => c.Name));

    [Theory]
    [InlineData("list")]
    [InlineData("folder-counts")]
    [InlineData("facets")]
    public void EachShelfCommandNeedsAnId(string name)
        => Assert.NotEmpty(Root().Parse(["systems", "books", name]).Errors);

    [Theory]
    [InlineData("list")]
    [InlineData("folder-counts")]
    public void TheFilteredCommandsTakeEveryShelfFilter(string name)
    {
        Assert.Empty(Root().Parse(
            ["systems", "books", name, "--id", "sys-1", "--query", "author:Kiesow", "--tags", "[\"x\"]",
             "--favorites", "--added-since", "2026-01-01T00:00:00Z", "--explicit", "false",
             "--genre", "__grim:none__", "--product-code", "PZO", "--category", "core",
             "--folder", "errata", "--sort", "product_code", "--order", "desc"]).Errors);
    }

    [Theory]
    [InlineData("list", "--sort", "name")]
    [InlineData("list", "--order", "up")]
    [InlineData("folder-counts", "--sort", "name")]
    [InlineData("folder-counts", "--order", "up")]
    public void AnUnknownSortOrOrderIsRejected(string name, string flag, string value)
        => Assert.NotEmpty(Root().Parse(["systems", "books", name, "--id", "sys-1", flag, value]).Errors);

    [Theory]
    [InlineData("0")]
    [InlineData("501")]
    public void ListBoundsTheLimit(string limit)
        => Assert.NotEmpty(Root().Parse(["systems", "books", "list", "--id", "sys-1", "--limit", limit]).Errors);

    [Fact]
    public void ListPagesWithLimitAndOffset()
        => Assert.Empty(Root().Parse(["systems", "books", "list", "--id", "sys-1", "--limit", "500", "--offset", "100"]).Errors);

    [Fact]
    public void FacetsTakesNoFilters()
        => Assert.NotEmpty(Root().Parse(["systems", "books", "facets", "--id", "sys-1", "--category", "core"]).Errors);

    [Fact]
    public void FolderNeedsCategoryIsSaid()
        => Assert.Contains("needs --category", Help(["systems", "books", "list"]));

    [Fact]
    public void TagSentinelsAreTaught()
        => Assert.Contains("match books with no tag", Help(["systems", "books", "list"]));

    [Fact]
    public void TagsMatchOnlyTheBooksOwnTagsIsSaid()
        => Assert.Contains("book's own tags only", Help(["systems", "books", "list"]));

    [Fact]
    public void TheQuerySyntaxIsTaught()
        => Assert.Contains("Query syntax", Help(["systems", "books", "folder-counts"]));

    [Fact]
    public void FolderCountsSaysCountsAreDirectOnly()
        => Assert.Contains("not its subfolders", Help(["systems", "books", "folder-counts"]));

    [Fact]
    public void FacetsSaysItIgnoresFilters()
        => Assert.Contains("before any filter", Help(["systems", "books", "facets"]));

    [Theory]
    [InlineData("list")]
    [InlineData("folder-counts")]
    [InlineData("facets")]
    public void TheShelfCommandsCarryNoRoleTag(string name)
        => Assert.DoesNotContain("Role required:", Help(["systems", "books", name]));

    [Fact]
    public void TheBooksSubgroupHostsFolders()
        => Assert.Contains("folders", Books().Subcommands.Select(c => c.Name));

    [Fact]
    public void SystemsNoLongerHostsBookFolders()
        => Assert.DoesNotContain("book-folders", SystemsCommand.Create().Subcommands.Select(c => c.Name));
}
