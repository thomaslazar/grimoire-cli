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

    [Theory]
    [InlineData("archive")]
    [InlineData("unarchive")]
    [InlineData("convert-to-group")]
    [InlineData("delete")]
    public void LifecycleCommandsExistAndRequireId(string verb)
    {
        Assert.Contains(CampaignsCommand.Create().Subcommands, c => c.Name == verb);
        Assert.NotEmpty(CampaignsCommand.Create().Parse([verb]).Errors);
        Assert.Empty(CampaignsCommand.Create().Parse([verb, "--id", "c"]).Errors);
        Assert.DoesNotContain("Role required:", RenderHelp(["campaigns", verb]));
    }

    [Fact]
    public void ConvertAcceptsGmTitle()
        => Assert.Empty(CampaignsCommand.Create().Parse(["convert-to-group", "--id", "c", "--gm-title", "Dungeon Master"]).Errors);

    [Fact]
    public void DeleteSaysThereIsNoUndo()
        => Assert.Contains("no undo", RenderHelp(["campaigns", "delete"]));

    [Fact]
    public void ConvertSaysItIsOneWay()
        => Assert.Contains("One-way", RenderHelp(["campaigns", "convert-to-group"]));
}
