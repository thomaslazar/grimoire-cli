using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class BookLinksCommandTests
{
    private static Command Links() => BooksCommand.Create().Subcommands.Single(c => c.Name == "links");

    private static string Help(string leaf) =>
        HelpRenderer.Render(BooksCommand.Create(), ["books", "links", leaf], full: false);

    [Theory]
    [InlineData("list")]
    [InlineData("add")]
    [InlineData("remove")]
    public void TheLinksSubgroupHostsItsThreeVerbs(string leaf)
        => Assert.Contains(leaf, Links().Subcommands.Select(c => c.Name));

    [Fact]
    public void LinksRequireTheirFlags()
    {
        var books = BooksCommand.Create();
        Assert.NotEmpty(books.Parse(["links", "list"]).Errors);
        Assert.Empty(books.Parse(["links", "list", "--id", "b1"]).Errors);
        Assert.NotEmpty(books.Parse(["links", "add", "--id", "b1"]).Errors);
        Assert.Empty(books.Parse(["links", "add", "--id", "b1", "--system-id", "s1"]).Errors);
        Assert.NotEmpty(books.Parse(["links", "remove", "--id", "b1"]).Errors);
        Assert.Empty(books.Parse(["links", "remove", "--id", "b1", "--system-id", "s1"]).Errors);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    public void WritesNeedGmOrAdmin(string leaf)
    {
        var output = Help(leaf);
        Assert.Contains("Role required:", output);
        Assert.Contains("gm or admin", output);
    }

    [Fact]
    public void ListNeedsNoRole()
        => Assert.DoesNotContain("Role required", Help("list"));
}
