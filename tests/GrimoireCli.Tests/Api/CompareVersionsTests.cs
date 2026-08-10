using GrimoireCli.Api;

namespace GrimoireCli.Tests.Api;

public class CompareVersionsTests
{
    [Fact]
    public void TreatsEqualVersionsAsEqual()
    {
        Assert.Equal(0, GrimoireApiClient.CompareVersions("1.5.4", "1.5.4"));
    }

    [Fact]
    public void OrdersNewerVersionsAbove()
    {
        Assert.True(GrimoireApiClient.CompareVersions("1.6.0", "1.5.4") > 0);
    }

    [Fact]
    public void ToleratesLeadingV()
    {
        Assert.True(GrimoireApiClient.CompareVersions("v1.5.3", "1.5.4") < 0);
    }

    [Fact]
    public void IgnoresPreReleaseSuffix()
    {
        Assert.Equal(0, GrimoireApiClient.CompareVersions("1.5.4-rc1", "1.5.4"));
    }

    [Fact]
    public void TreatsMissingSegmentsAsZero()
    {
        Assert.Equal(0, GrimoireApiClient.CompareVersions("1.5", "1.5.0"));
    }

    // An unparseable version must not throw — it would take down a working command.
    [Fact]
    public void TreatsUnparseableSegmentsAsZero()
    {
        Assert.Equal(0, GrimoireApiClient.CompareVersions("dev", "0.0.0"));
    }

    [Fact]
    public void OneFiveFiveIsNewerThanOneFiveFour()
    {
        Assert.True(GrimoireApiClient.CompareVersions("1.5.5", "1.5.4") > 0);
    }
}
