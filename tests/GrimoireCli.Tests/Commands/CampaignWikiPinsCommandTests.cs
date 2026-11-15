using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignWikiPinsCommandTests
{
    private static string RenderHelp(string verb) =>
        HelpRenderer.Render(CampaignsCommand.Create(), ["campaigns", "wiki", "pins", verb], full: false);

    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        CampaignsCommand.Create().Parse(["wiki", "pins", .. args]);

    [Fact]
    public void CreateNeedsBothCoordinates()
        => Assert.NotEmpty(Parse("create", "--id", "c", "--page-id", "p", "--x", "0.5").Errors);

    [Fact]
    public void CreateParsesWithCoordinates()
        => Assert.Empty(Parse("create", "--id", "c", "--page-id", "p", "--x", "0.5", "--y", "0.5").Errors);

    [Fact]
    public void VisibilityRejectsAnUnknownValue()
        => Assert.NotEmpty(Parse("create", "--id", "c", "--page-id", "p", "--x", "0", "--y", "0", "--visibility", "public").Errors);

    [Fact]
    public void VisibilityAcceptsGm()
        => Assert.Empty(Parse("update", "--id", "c", "--page-id", "p", "--pin-id", "k", "--visibility", "gm").Errors);

    [Fact]
    public void UpdateNeedsSomethingToChange()
        => Assert.NotEmpty(Parse("update", "--id", "c", "--page-id", "p", "--pin-id", "k").Errors);

    [Fact]
    public void UpdateAcceptsAnEmptyTargetPageId()
        => Assert.Empty(Parse("update", "--id", "c", "--page-id", "p", "--pin-id", "k", "--target-page-id", "").Errors);

    [Fact]
    public void DeleteNeedsAPinId()
        => Assert.NotEmpty(Parse("delete", "--id", "c", "--page-id", "p").Errors);

    [Fact]
    public void NoPinsCommandCarriesARoleTag()
    {
        foreach (var verb in new[] { "create", "update", "delete" })
            Assert.DoesNotContain("Role required:", RenderHelp(verb));
    }

    [Fact]
    public void HelpSaysOnlyTheGmMakesAPinGmOnly()
    {
        foreach (var verb in new[] { "create", "update" })
            Assert.Contains("Only the campaign GM", RenderHelp(verb));
    }

    [Fact]
    public void UpdateTeachesUnlinking() => Assert.Contains("--target-page-id \"\" unlinks", RenderHelp("update"));

    [Fact]
    public void WikiCreateHelpCarriesTheMapPageRules()
        => Assert.Contains("linked to the campaign", HelpRenderer.Render(CampaignsCommand.Create(), ["campaigns", "wiki", "create"], full: false));
}
