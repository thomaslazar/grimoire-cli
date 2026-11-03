using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class BookCoverCommandTests
{
    [Theory]
    [InlineData("upload")]
    [InlineData("delete")]
    [InlineData("from-source")]
    public void TheCoverSubgroupHostsItsThreeVerbs(string leaf)
    {
        var cover = BooksCommand.Create().Subcommands.Single(c => c.Name == "cover");
        Assert.Contains(leaf, cover.Subcommands.Select(c => c.Name));
    }

    // Reading the cover stays books thumbnail: the book has no GET on /cover.
    [Fact]
    public void TheCoverSubgroupHasNoGet()
    {
        var cover = BooksCommand.Create().Subcommands.Single(c => c.Name == "cover");
        Assert.DoesNotContain("get", cover.Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void CoverWritesRequireTheirFlags()
    {
        var books = BooksCommand.Create();
        Assert.NotEmpty(books.Parse(["cover", "upload", "--id", "b1"]).Errors);
        Assert.Empty(books.Parse(["cover", "upload", "--id", "b1", "--file", "c.png"]).Errors);
        Assert.Empty(books.Parse(["cover", "delete", "--id", "b1"]).Errors);
        Assert.NotEmpty(books.Parse(["cover", "from-source", "--id", "b1", "--source-type", "map"]).Errors);
        Assert.Empty(books.Parse(
            ["cover", "from-source", "--id", "b1", "--source-type", "map", "--source-id", "m1"]).Errors);
    }
}
