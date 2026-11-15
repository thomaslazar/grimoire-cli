using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class LeagueBooksCommandTests
{
    private static string Help(string verb) => HelpRenderer.Render(LeagueBooksCommands.Create(), ["books", verb], full: false);

    private static System.CommandLine.ParseResult Parse(params string[] args) => LeagueBooksCommands.Create().Parse(args);

    [Fact]
    public void RejectsAnUnknownVisibility() =>
        Assert.NotEmpty(Parse("add", "--id", "l", "--book-id", "b", "--visibility", "everyone").Errors);

    [Fact]
    public void AddAcceptsOrganizer() =>
        Assert.Empty(Parse("add", "--id", "l", "--book-id", "b", "--visibility", "organizer", "--shared-user-ids", "u1", "u2").Errors);

    [Fact]
    public void UpdateNeedsAFlag() =>
        Assert.NotEmpty(Parse("update", "--id", "l", "--resource-id", "r").Errors);

    [Fact]
    public void UpdateAcceptsEitherFlag()
    {
        Assert.Empty(Parse("update", "--id", "l", "--resource-id", "r", "--visibility", "public").Errors);
        Assert.Empty(Parse("update", "--id", "l", "--resource-id", "r", "--shared-user-ids", "u1").Errors);
    }

    [Fact]
    public void SearchNeedsAQuery() => Assert.NotEmpty(Parse("search", "--id", "l").Errors);

    [Fact]
    public void ReorderNeedsOrderedIds() => Assert.NotEmpty(Parse("reorder", "--id", "l").Errors);

    [Theory]
    [InlineData("update")]
    [InlineData("remove")]
    [InlineData("reorder")]
    public void SaysToUseTheListId(string verb) => Assert.Contains("book_id", Help(verb));

    [Fact]
    public void NoBooksCommandCarriesARoleTag()
    {
        foreach (var verb in new[] { "list", "search", "add", "update", "remove", "reorder" })
            Assert.DoesNotContain("Role required:", Help(verb));
    }
}
