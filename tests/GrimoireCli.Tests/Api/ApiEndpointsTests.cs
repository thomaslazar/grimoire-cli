using GrimoireCli.Api;

namespace GrimoireCli.Tests.Api;

public class ApiEndpointsTests
{
    // An unencoded "../about" normalises the request off /api/systems and onto
    // /api/about, returning an unrelated 200 body instead of a 404.
    [Fact]
    public void SystemEscapesPathTraversal()
    {
        Assert.DoesNotContain("../", ApiEndpoints.System("../about"));
    }

    [Fact]
    public void SystemEscapesEmptyAndDotSegments()
    {
        Assert.Equal("api/systems/", ApiEndpoints.System(""));
        Assert.Equal("api/systems/.", ApiEndpoints.System("."));
    }

    [Fact]
    public void SystemLeavesOrdinaryIdsUnchanged()
    {
        Assert.Equal("api/systems/abc-123", ApiEndpoints.System("abc-123"));
    }
}
