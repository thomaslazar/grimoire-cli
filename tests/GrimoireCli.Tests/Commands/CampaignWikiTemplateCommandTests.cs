using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignWikiTemplateCommandTests
{
    private static string Help(string verb) =>
        HelpRenderer.Render(CampaignsCommand.Create(), ["campaigns", "wiki", "templates", verb], full: false);

    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        CampaignsCommand.Create().Parse(["wiki", "templates", .. args]);

    [Fact]
    public void TheTemplatesSubgroupHoldsTheTemplateCommands()
    {
        var wiki = CampaignsCommand.Create().Subcommands.Single(c => c.Name == "wiki");
        var templates = wiki.Subcommands.Single(c => c.Name == "templates");
        Assert.Equal(
            new[] { "catalogue", "create", "delete", "export", "get", "import", "install", "list", "update", "use" },
            templates.Subcommands.Select(c => c.Name).Order());
    }

    [Theory]
    [InlineData("get")]
    [InlineData("delete")]
    [InlineData("use")]
    public void TheTemplateCommandsNeedATemplateId(string verb)
    {
        Assert.NotEmpty(Parse(verb, "--id", "c").Errors);
        Assert.Empty(Parse(verb, "--id", "c", "--template-id", "t").Errors);
    }

    [Fact]
    public void UpdateNeedsATemplateId()
        => Assert.NotEmpty(Parse("update", "--id", "c", "--stdin").Errors);

    [Fact]
    public void ExportNeedsATemplateIdAndAnOutput()
    {
        Assert.NotEmpty(Parse("export", "--id", "c", "--output", "-").Errors);
        Assert.NotEmpty(Parse("export", "--id", "c", "--template-id", "t").Errors);
        Assert.Empty(Parse("export", "--id", "c", "--template-id", "t", "--output", "-").Errors);
    }

    [Fact]
    public void CreateNeedsABodySource()
    {
        Assert.NotEmpty(Parse("create", "--id", "c").Errors);
        Assert.Empty(Parse("create", "--id", "c", "--stdin").Errors);
    }

    [Fact]
    public void ImportNeedsAFile()
        => Assert.NotEmpty(Parse("import", "--id", "c").Errors);

    [Fact]
    public void UpdateSaysDefaultsReplacesAllFive()
        => Assert.Contains("replaces all five", Help("update"));

    [Fact]
    public void UseSaysSessionDateIsNeverSet()
        => Assert.Contains("session_date is never set", Help("use"));

    [Fact]
    public void ImportSaysTheSizeCap()
        => Assert.Contains("Max 512 KB", Help("import"));

    [Fact]
    public void InstallNeedsASourceId()
    {
        Assert.NotEmpty(Parse("install", "--id", "c").Errors);
        Assert.Empty(Parse("install", "--id", "c", "--source-id", "smoke-note", "--index-url", "http://x/templates/index.json").Errors);
    }

    [Fact]
    public void CatalogueTakesRefresh()
        => Assert.Empty(Parse("catalogue", "--id", "c", "--refresh").Errors);

    [Fact]
    public void CatalogueSaysEntriesCarryNoBody()
        => Assert.Contains("Entries carry no template body", Help("catalogue"));

    [Fact]
    public void InstallSaysItMakesAnotherCopy()
        => Assert.Contains("another copy", Help("install"));
}
