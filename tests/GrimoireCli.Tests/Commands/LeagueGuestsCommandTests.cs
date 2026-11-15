using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class LeagueGuestsCommandTests
{
    private static string Help(string verb) => HelpRenderer.Render(LeagueGuestsCommands.Create(), ["guests", verb], full: false);

    private static System.CommandLine.ParseResult Parse(params string[] args) => LeagueGuestsCommands.Create().Parse(args);

    [Theory]
    [InlineData("regenerate")]
    [InlineData("share")]
    [InlineData("remove")]
    public void NeedsAUserId(string verb) => Assert.NotEmpty(Parse(verb, "--id", "l").Errors);

    [Fact]
    public void CreateTakesAnOptionalNickname() => Assert.Empty(Parse("create", "--id", "l").Errors);

    [Fact]
    public void RemoveSaysTheAccountStays() => Assert.Contains("account", Help("remove"));

    [Fact]
    public void NoGuestsCommandCarriesARoleTag()
    {
        foreach (var verb in new[] { "list", "create", "regenerate", "share", "remove" })
            Assert.DoesNotContain("Role required:", Help(verb));
    }
}
