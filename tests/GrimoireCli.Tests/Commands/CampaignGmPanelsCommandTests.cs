using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignGmPanelsCommandTests
{
    private static string RenderHelp(string verb) =>
        HelpRenderer.Render(CampaignsCommand.Create(), ["campaigns", "gm-screen", "panels", verb], full: false);

    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        CampaignsCommand.Create().Parse(["gm-screen", "panels", .. args]);

    [Fact]
    public void UpdateNeedsAPanelId()
        => Assert.NotEmpty(Parse("update", "--id", "c", "--stdin").Errors);

    [Fact]
    public void CreateNeedsABodySource()
        => Assert.NotEmpty(Parse("create", "--id", "c").Errors);

    [Fact]
    public void NoVerbCarriesARoleTag()
    {
        foreach (var verb in new[] { "create", "update", "delete" })
            Assert.DoesNotContain("Role required:", RenderHelp(verb));
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    public void BodyCommandsTeachThePanelKinds(string verb)
    {
        var help = RenderHelp(verb);
        Assert.Contains("initiative", help);
        Assert.Contains("silently become defaults", help);
    }

    [Fact]
    public void UpdateStatesStateReplacement()
        => Assert.Contains("replaces the panel's state whole", RenderHelp("update"));
}
