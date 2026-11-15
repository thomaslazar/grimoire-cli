using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class LeaguesCommandTests
{
    private static string RenderHelp(params string[] verb) =>
        HelpRenderer.Render(LeaguesCommand.Create(), ["leagues", .. verb], full: false);

    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        LeaguesCommand.Create().Parse(args);

    [Fact]
    public void CreateNeedsABodySource() => Assert.NotEmpty(Parse("create").Errors);

    [Fact]
    public void UpdateNeedsAnId() => Assert.NotEmpty(Parse("update", "--stdin").Errors);

    [Fact]
    public void OnlyCreateCarriesTheRoleTag()
    {
        Assert.Contains("gm or admin", RenderHelp("create"));
        foreach (var verb in new[] { "list", "get", "update", "delete", "eligible" })
            Assert.DoesNotContain("Role required:", RenderHelp(verb));
        foreach (var verb in new[] { "add", "remove", "army" })
            Assert.DoesNotContain("Role required:", RenderHelp("members", verb));
    }

    [Fact]
    public void UpdateSaysFinishedIsOnlyALabel() => Assert.Contains("label", RenderHelp("update"));
}
