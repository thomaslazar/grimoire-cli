using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignCalendarCommandTests
{
    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        CampaignsCommand.Create().Parse(["calendar", .. args]);

    private static System.CommandLine.Command Calendar() =>
        CampaignsCommand.Create().Subcommands.Single(c => c.Name == "calendar");

    [Fact]
    public void TheGroupHoldsDownloadAndSubscription()
        => Assert.Equal(new[] { "download", "subscription" }, Calendar().Subcommands.Select(c => c.Name).Order());

    [Fact]
    public void SubscriptionHoldsGetCreateRevoke()
        => Assert.Equal(new[] { "create", "get", "revoke" },
            Calendar().Subcommands.Single(c => c.Name == "subscription").Subcommands.Select(c => c.Name).Order());

    [Fact]
    public void DownloadNeedsOutput()
    {
        Assert.NotEmpty(Parse("download", "--id", "c").Errors);
        Assert.Empty(Parse("download", "--id", "c", "--output", "-").Errors);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("create")]
    public void SubscriptionIdIsOptional(string verb)
    {
        Assert.Empty(Parse("subscription", verb).Errors);
        Assert.Empty(Parse("subscription", verb, "--id", "c").Errors);
    }

    [Fact]
    public void RevokeTakesNoId()
    {
        Assert.Empty(Parse("subscription", "revoke").Errors);
        Assert.NotEmpty(Parse("subscription", "revoke", "--id", "c").Errors);
    }
}
