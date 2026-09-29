using GrimoireCli.Services;

namespace GrimoireCli.Tests.Services;

public class CampaignResourcesServiceTests
{
    [Fact]
    public void OmittedFlagsLeaveTheUpdateBodyEmpty()
    {
        var body = CampaignResourcesService.BuildUpdateBody(visibility: null, categoryId: null);
        Assert.Null(body.Visibility);
        Assert.Null(body.CategoryId);
    }

    // "" is the server's sentinel for "back to the built-in type group".
    [Fact]
    public void EmptyCategoryIdIsSentToClear()
        => Assert.Equal("", CampaignResourcesService.BuildUpdateBody(null, "").CategoryId!.String);

    [Fact]
    public void GivenVisibilityReachesTheBody()
        => Assert.Equal("public", CampaignResourcesService.BuildUpdateBody("public", null).Visibility!.String);
}
