using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignScheduleCommandTests
{
    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        CampaignsCommand.Create().Parse(args);

    private static System.CommandLine.Command Group(string name) =>
        CampaignsCommand.Create().Subcommands.Single(c => c.Name == name);

    [Fact]
    public void TheGroupsHoldTheirCommands()
    {
        Assert.Equal(new[] { "delete", "get", "set" }, Group("schedule").Subcommands.Select(c => c.Name).Order());
        Assert.Equal(new[] { "get", "set", "toggle-cancel" }, Group("availability").Subcommands.Select(c => c.Name).Order());
    }

    [Theory]
    [InlineData("schedule", "get")]
    [InlineData("schedule", "delete")]
    [InlineData("schedule", "set", "--stdin")]
    [InlineData("availability", "get")]
    [InlineData("availability", "set", "--date", "2026-10-21", "--status", "available")]
    [InlineData("availability", "toggle-cancel", "--date", "2026-10-21")]
    public void EveryCommandNeedsACampaignId(params string[] args)
    {
        Assert.NotEmpty(Parse(args).Errors);
        Assert.Empty(Parse([args[0], args[1], "--id", "c", .. args[2..]]).Errors);
    }

    [Fact]
    public void ScheduleSetNeedsABodySource()
        => Assert.NotEmpty(Parse("schedule", "set", "--id", "c").Errors);

    [Fact]
    public void AvailabilitySetNeedsADateAndAStatus()
    {
        Assert.NotEmpty(Parse("availability", "set", "--id", "c", "--status", "available").Errors);
        Assert.NotEmpty(Parse("availability", "set", "--id", "c", "--date", "2026-10-21").Errors);
    }

    [Fact]
    public void AvailabilitySetRejectsAnUnknownStatus()
        => Assert.NotEmpty(Parse("availability", "set", "--id", "c", "--date", "2026-10-21", "--status", "maybe").Errors);

    [Fact]
    public void AvailabilitySetTakesAllFlags()
        => Assert.Empty(Parse("availability", "set", "--id", "c", "--date", "2026-10-21", "--status", "available", "--is-cancelled", "true", "--user-id", "u").Errors);

    [Fact]
    public void ToggleCancelHelpSaysItFlips()
        => Assert.Contains("flips", HelpRenderer.Render(CampaignsCommand.Create(), ["campaigns", "availability", "toggle-cancel"], full: false), StringComparison.OrdinalIgnoreCase);
}
