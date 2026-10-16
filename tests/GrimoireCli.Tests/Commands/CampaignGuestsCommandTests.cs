using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignGuestsCommandTests
{
    private static System.CommandLine.Command Group() =>
        CampaignsCommand.Create().Subcommands.Single(c => c.Name == "guests");

    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        CampaignsCommand.Create().Parse(["guests", .. args]);

    [Fact]
    public void TheGroupHoldsTheGuestCommands()
        => Assert.Equal(new[] { "create", "list", "regenerate", "remove", "share" }, Group().Subcommands.Select(c => c.Name).Order());

    [Theory]
    [InlineData("regenerate")]
    [InlineData("remove")]
    [InlineData("share")]
    public void PerGuestCommandsNeedAMemberId(string verb)
    {
        Assert.NotEmpty(Parse(verb, "--id", "c").Errors);
        Assert.Empty(Parse(verb, "--id", "c", "--member-id", "m").Errors);
    }

    [Fact]
    public void CreateTakesAnOptionalNickname()
    {
        Assert.Empty(Parse("create", "--id", "c").Errors);
        Assert.Empty(Parse("create", "--id", "c", "--nickname", "Sam").Errors);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("list")]
    [InlineData("regenerate")]
    [InlineData("share")]
    public void HelpWarnsThatTheCodeIsTheLogin(string verb)
    {
        var root = new System.CommandLine.RootCommand { CampaignsCommand.Create() };
        root.UseCustomHelpSections();
        var writer = new StringWriter();
        root.Parse(["campaigns", "guests", verb, "--help"]).Invoke(new System.CommandLine.InvocationConfiguration { Output = writer });
        Assert.Contains("anyone holding it is that guest", writer.ToString().Replace("\n", " ").Replace("  ", " "));
    }
}
