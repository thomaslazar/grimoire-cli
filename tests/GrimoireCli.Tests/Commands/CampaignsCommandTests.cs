using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignsCommandTests
{
    private static string RenderHelp(string[] path, bool full = false) =>
        HelpRenderer.Render(CampaignsCommand.Create(), path, full);

    [Fact]
    public void NoCampaignCommandCarriesARoleTag()
    {
        foreach (var verb in new[] { "list", "get", "create", "update" })
            Assert.DoesNotContain("Role required:", RenderHelp(["campaigns", verb]));
    }

    [Fact]
    public void UpdateCarriesTheOwnerCaveat()
        => Assert.Contains("Owner only, no admin override", RenderHelp(["campaigns", "update"]));

    [Fact]
    public void CreateSaysGmCampaignsNeedARole()
        => Assert.Contains("is_gm_campaign", RenderHelp(["campaigns", "create"]));

    [Fact]
    public void ListSaysArchivedAreHidden()
        => Assert.Contains("--include-archived", RenderHelp(["campaigns", "list"]));

    [Fact]
    public void UpdateRequiresId()
        => Assert.NotEmpty(CampaignsCommand.Create().Parse(["update", "--stdin"]).Errors);
}
