using GrimoireCli.Api;

namespace GrimoireCli.Tests.Api;

public class VersionCheckCadenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AMissingTimestampIsDue() => Assert.True(GrimoireApiClient.ShouldCheckVersion(null, Now));

    [Fact]
    public void JustUnderTheIntervalIsNotDue()
        => Assert.False(GrimoireApiClient.ShouldCheckVersion(Now.AddHours(-23), Now));

    // The boundary is >=, so exactly the interval is due.
    [Fact]
    public void ExactlyTheIntervalIsDue()
        => Assert.True(GrimoireApiClient.ShouldCheckVersion(Now - GrimoireApiClient.VersionCheckInterval, Now));

    [Fact]
    public void PastTheIntervalIsDue()
        => Assert.True(GrimoireApiClient.ShouldCheckVersion(Now.AddHours(-25), Now));

    // A clock that moved backwards would otherwise park the check in the future
    // forever, never checking again.
    [Fact]
    public void ATimestampInTheFutureIsDue()
        => Assert.True(GrimoireApiClient.ShouldCheckVersion(Now.AddHours(1), Now));

    [Fact]
    public void TheIntervalIsADay() => Assert.Equal(TimeSpan.FromHours(24), GrimoireApiClient.VersionCheckInterval);

    [Fact]
    public void AnInRangeVersionWarnsAboutNothing()
        => Assert.Null(GrimoireApiClient.VersionWarning("1.5.6", previous: "1.5.6"));

    [Fact]
    public void AnUnknownVersionWarnsAboutNothing()
        => Assert.Null(GrimoireApiClient.VersionWarning(null, previous: null));

    [Fact]
    public void ANewerServerNamesBothVersionsAndTheClient()
    {
        var warning = GrimoireApiClient.VersionWarning("1.6.0", previous: null);
        Assert.NotNull(warning);
        Assert.Contains("1.6.0", warning);
        Assert.Contains("1.5.6", warning);
        Assert.Contains(GrimoireApiClient.ClientVersion, warning);
        Assert.Contains("newer grimoire-cli", warning);
    }

    [Fact]
    public void AnOlderServerWarnsAboutTheFloor()
    {
        var warning = GrimoireApiClient.VersionWarning("1.4.0", previous: null);
        Assert.NotNull(warning);
        Assert.Contains("1.4.0", warning);
        Assert.Contains("older", warning);
    }

    // The operator's real signal is that the server moved, so say so.
    [Fact]
    public void AChangedVersionSaysItMoved()
    {
        var warning = GrimoireApiClient.VersionWarning("1.6.0", previous: "1.5.6");
        Assert.NotNull(warning);
        Assert.Contains("moved", warning);
        Assert.Contains("1.5.6", warning);
        Assert.Contains("1.6.0", warning);
    }

    // An unchanged in-range version stays silent even across checks.
    [Fact]
    public void AnUnchangedInRangeVersionStaysSilent()
        => Assert.Null(GrimoireApiClient.VersionWarning("1.5.6", previous: "1.5.6"));
}
