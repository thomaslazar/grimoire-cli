using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class ArmyPublicationLinksCommandTests
{
    private static string RenderHelp(string verb) =>
        HelpRenderer.Render(ArmiesCommand.Create(), ["armies", "publication-links", verb], full: false);

    [Theory]
    [InlineData("set")]
    [InlineData("remove")]
    [InlineData("books")]
    public void EveryVerbCarriesTheRoleTag(string verb)
        => Assert.Contains("Role required:\n  gm or admin\n", RenderHelp(verb));

    [Fact]
    public void BooksNeedsAQuery()
        => Assert.NotEmpty(ArmiesCommand.Create().Parse(["publication-links", "books"]).Errors);
}
