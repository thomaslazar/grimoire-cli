using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class LeagueGamesCommandTests
{
    private static string Help(string verb) => HelpRenderer.Render(LeagueGamesCommands.Create(), ["games", verb], full: false);

    private static System.CommandLine.ParseResult Parse(params string[] args) => LeagueGamesCommands.Create().Parse(args);

    [Fact]
    public void CreateNeedsABodySource() => Assert.NotEmpty(Parse("create", "--id", "l").Errors);

    [Fact]
    public void UpdateNeedsAGameId() => Assert.NotEmpty(Parse("update", "--id", "l", "--stdin").Errors);

    [Fact]
    public void DeleteNeedsAGameId() => Assert.NotEmpty(Parse("delete", "--id", "l").Errors);

    [Fact]
    public void UpdateSaysItReplacesTheGame() => Assert.Contains("replaces", Help("update"));

    [Fact]
    public void NoGamesCommandCarriesARoleTag()
    {
        foreach (var verb in new[] { "create", "update", "delete" })
            Assert.DoesNotContain("Role required:", Help(verb));
    }
}
