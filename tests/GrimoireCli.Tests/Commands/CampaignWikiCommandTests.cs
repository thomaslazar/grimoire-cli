using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignWikiCommandTests
{
    private static string Help(string verb) =>
        HelpRenderer.Render(CampaignsCommand.Create(), ["campaigns", "wiki", verb], full: false);

    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        CampaignsCommand.Create().Parse(["wiki", .. args]);

    [Theory]
    [InlineData("list")]
    [InlineData("get")]
    [InlineData("search")]
    [InlineData("titles")]
    public void TheWikiSubgroupHostsTheReadCommands(string verb)
        => Assert.Contains(verb, CampaignsCommand.Create().Subcommands.Single(c => c.Name == "wiki").Subcommands.Select(c => c.Name));

    [Fact]
    public void ListNeedsTheCampaign()
        => Assert.NotEmpty(Parse("list").Errors);

    [Fact]
    public void ListTakesMineAndIncludeHidden()
        => Assert.Empty(Parse("list", "--id", "c", "--mine", "--include-hidden").Errors);

    [Fact]
    public void GetNeedsThePage()
        => Assert.NotEmpty(Parse("get", "--id", "c").Errors);

    [Theory]
    [InlineData("")]
    public void SearchRejectsAnEmptyQuery(string query)
        => Assert.NotEmpty(Parse("search", "--id", "c", "--query", query).Errors);

    [Fact]
    public void SearchRejectsAQueryOver200Characters()
        => Assert.NotEmpty(Parse("search", "--id", "c", "--query", new string('x', 201)).Errors);

    [Fact]
    public void SearchAcceptsA200CharacterQuery()
        => Assert.Empty(Parse("search", "--id", "c", "--query", new string('x', 200)).Errors);

    [Fact]
    public void GetSaysNonOwnersGetSecretsStripped()
        => Assert.Contains("GM secrets removed", Help("get"));

    [Fact]
    public void SearchSaysItIsAPlainSubstring()
        => Assert.Contains("plain substring", Help("search"));

    [Theory]
    [InlineData("list")]
    [InlineData("get")]
    [InlineData("search")]
    [InlineData("titles")]
    public void TheReadCommandsCarryNoRoleTag(string verb)
        => Assert.DoesNotContain("Role required:", Help(verb));
}
