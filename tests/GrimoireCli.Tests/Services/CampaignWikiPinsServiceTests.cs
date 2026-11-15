using GrimoireCli.Services;

namespace GrimoireCli.Tests.Services;

public class CampaignWikiPinsServiceTests
{
    [Fact]
    public void CreateBodyWithOnlyCoordinatesLeavesTheRestUnset()
    {
        var body = CampaignWikiPinsService.BuildCreateBody(0.25, 0.5, null, null, null, null, null, null);
        Assert.Equal(0.25, body.X);
        Assert.Equal(0.5, body.Y);
        Assert.Null(body.Label);
        Assert.Null(body.TargetPageId);
        Assert.Null(body.TargetTitle);
        Assert.Null(body.Icon);
        Assert.Null(body.IconColor);
        Assert.Null(body.Visibility);
    }

    [Fact]
    public void CreateBodyCarriesEveryFlagGiven()
    {
        var body = CampaignWikiPinsService.BuildCreateBody(0, 1, "Gate", "p1", "Castle", "castle", "#ff0000", "gm");
        Assert.Equal("Gate", body.Label);
        Assert.Equal("p1", body.TargetPageId!.String);
        Assert.Equal("Castle", body.TargetTitle!.String);
        Assert.Equal("castle", body.Icon!.String);
        Assert.Equal("#ff0000", body.IconColor!.String);
        Assert.Equal("gm", body.Visibility);
    }

    [Fact]
    public void OmittedFlagsLeaveTheUpdateBodyEmpty()
    {
        var body = CampaignWikiPinsService.BuildUpdateBody(null, null, null, null, null, null, null, null);
        Assert.Null(body.X);
        Assert.Null(body.Y);
        Assert.Null(body.Label);
        Assert.Null(body.TargetPageId);
        Assert.Null(body.TargetTitle);
        Assert.Null(body.Icon);
        Assert.Null(body.IconColor);
        Assert.Null(body.Visibility);
    }

    [Fact]
    public void UpdateBodyCarriesOnlyTheFlagsGiven()
    {
        var body = CampaignWikiPinsService.BuildUpdateBody(0.75, null, null, null, null, null, null, "gm");
        Assert.Equal(0.75, body.X!.Double);
        Assert.Null(body.Y);
        Assert.Equal("gm", body.Visibility!.String);
    }

    [Fact]
    public void EmptyTargetPageIdIsSentToUnlink()
        => Assert.Equal("", CampaignWikiPinsService.BuildUpdateBody(null, null, null, "", null, null, null, null).TargetPageId!.String);

    [Fact]
    public void EmptyIconIsSentToClear()
        => Assert.Equal("", CampaignWikiPinsService.BuildUpdateBody(null, null, null, null, null, "", null, null).Icon!.String);
}
