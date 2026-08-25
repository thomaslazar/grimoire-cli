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

    // A version with no numeric component tells us nothing, so there is nothing to
    // compare it against — the nightly channel reports the literal "nightly".
    [Theory]
    [InlineData("1.5.6", true)]
    [InlineData("1.5.6-tk8i6j", true)]
    [InlineData("v1.6.0", true)]
    [InlineData("2", true)]
    [InlineData("nightly", false)]
    [InlineData("edge", false)]
    [InlineData("dev", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsComparableVersion_RequiresANumericComponent(string? version, bool expected)
        => Assert.Equal(expected, GrimoireApiClient.IsComparableVersion(version));
}
