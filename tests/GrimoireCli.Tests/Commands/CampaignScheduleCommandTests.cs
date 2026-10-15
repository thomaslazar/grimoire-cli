using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignScheduleCommandTests
{
    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        CampaignsCommand.Create().Parse(args);

    [Fact]
    public void TheGroupHoldsTheScheduleCommands()
        => Assert.Equal(new[] { "delete", "get", "set" },
            CampaignsCommand.Create().Subcommands.Single(c => c.Name == "schedule").Subcommands.Select(c => c.Name).Order());

    [Theory]
    [InlineData("get")]
    [InlineData("delete")]
    [InlineData("set", "--stdin")]
    public void EveryCommandNeedsACampaignId(params string[] args)
    {
        Assert.NotEmpty(Parse(["schedule", .. args]).Errors);
        Assert.Empty(Parse(["schedule", args[0], "--id", "c", .. args[1..]]).Errors);
    }

    [Fact]
    public void SetNeedsABodySource()
        => Assert.NotEmpty(Parse("schedule", "set", "--id", "c").Errors);
}
