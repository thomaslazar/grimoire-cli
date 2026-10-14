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
    public void CreateSaysOmittedVisibilityIsGm()
        => Assert.Contains("Omitted on create, it is gm.", Help("create"));

    [Fact]
    public void TitlesShowsThePinnedLinkForm()
        => Assert.Contains("[[Title:id-<page-id>]]", Help("titles"));

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

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("reorder")]
    [InlineData("hide")]
    [InlineData("unhide")]
    public void TheWikiSubgroupHostsTheWriteCommands(string verb)
        => Assert.Contains(verb, CampaignsCommand.Create().Subcommands.Single(c => c.Name == "wiki").Subcommands.Select(c => c.Name));

    [Fact]
    public void CreateNeedsExactlyOneBodySource()
    {
        Assert.NotEmpty(Parse("create", "--id", "c").Errors);
        Assert.NotEmpty(Parse("create", "--id", "c", "--stdin", "--input", "p.json").Errors);
        Assert.Empty(Parse("create", "--id", "c", "--stdin").Errors);
    }

    [Fact]
    public void UpdateNeedsThePageAndABody()
    {
        Assert.NotEmpty(Parse("update", "--id", "c", "--stdin").Errors);
        Assert.Empty(Parse("update", "--id", "c", "--page-id", "p", "--stdin").Errors);
    }

    [Fact]
    public void AnUnknownCreateFieldIsRejected()
        => Assert.Throws<BodyInputException>(() =>
            JsonBodyInput.Validate("{\"title\":\"T\",\"content\":\"x\"}",
                Generated.Models.WikiPageCreate.CreateFromDiscriminatorValue, "pass it with --id"));

    [Fact]
    public void AnUnknownUpdateFieldIsRejected()
        => Assert.Throws<BodyInputException>(() =>
            JsonBodyInput.Validate("{\"name\":\"T\"}",
                Generated.Models.WikiPageUpdate.CreateFromDiscriminatorValue, "pass it with --page-id"));

    [Fact]
    public void ReorderNeedsTheIds()
        => Assert.NotEmpty(Parse("reorder", "--id", "c").Errors);

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    public void TheBodyCommandsTeachLinksEmbedsAndSecrets(string verb)
    {
        var help = Help(verb);
        Assert.Contains("Page body:", help);
        Assert.Contains("creates an empty stub", help);
        Assert.Contains("[[model:<id>]]", help);
        Assert.Contains("[[image:<file-id>]]", help);
        Assert.Contains("||text|| is a GM secret", help);
        Assert.Contains("Visibility:", help);
    }

    [Fact]
    public void DeleteSaysChildrenMoveUp()
        => Assert.Contains("children move up", Help("delete"));

    [Fact]
    public void HideSaysItIsRefusedInAPersonalCampaign()
        => Assert.Contains("personal campaign", Help("hide"));

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("reorder")]
    [InlineData("hide")]
    [InlineData("unhide")]
    public void TheWriteCommandsCarryNoRoleTag(string verb)
        => Assert.DoesNotContain("Role required:", Help(verb));
}
