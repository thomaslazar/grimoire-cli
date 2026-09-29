using GrimoireCli.Services;

namespace GrimoireCli.Tests.Services;

public class CampaignCategoriesServiceTests
{
    [Fact]
    public void OmittedFlagsLeaveTheUpdateBodyEmpty()
    {
        var body = CampaignCategoriesService.BuildUpdateBody(null, null, null);
        Assert.Null(body.Name);
        Assert.Null(body.Icon);
        Assert.Null(body.IconColor);
    }

    [Fact]
    public void EmptyIconIsSentToClear()
        => Assert.Equal("", CampaignCategoriesService.BuildUpdateBody(null, "", null).Icon!.String);

    [Fact]
    public void CreateBodyIsAlwaysAResourceCategory()
        => Assert.Equal("resource", CampaignCategoriesService.BuildCreateBody("Handouts", null, null).Kind);
}
