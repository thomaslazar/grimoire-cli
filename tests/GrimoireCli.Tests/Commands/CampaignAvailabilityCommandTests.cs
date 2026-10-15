using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignAvailabilityCommandTests
{
    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        CampaignsCommand.Create().Parse(args);

    [Fact]
    public void TheGroupHoldsTheAvailabilityCommands()
        => Assert.Equal(new[] { "get", "set", "toggle-cancel" },
            CampaignsCommand.Create().Subcommands.Single(c => c.Name == "availability").Subcommands.Select(c => c.Name).Order());

    [Theory]
    [InlineData("get")]
    [InlineData("set", "--date", "2026-10-21", "--status", "available")]
    [InlineData("toggle-cancel", "--date", "2026-10-21")]
    public void EveryCommandNeedsACampaignId(params string[] args)
    {
        Assert.NotEmpty(Parse(["availability", .. args]).Errors);
        Assert.Empty(Parse(["availability", args[0], "--id", "c", .. args[1..]]).Errors);
    }

    [Fact]
    public void SetNeedsADateAndAStatus()
    {
        Assert.NotEmpty(Parse("availability", "set", "--id", "c", "--status", "available").Errors);
        Assert.NotEmpty(Parse("availability", "set", "--id", "c", "--date", "2026-10-21").Errors);
    }

    [Fact]
    public void SetRejectsAnUnknownStatus()
        => Assert.NotEmpty(Parse("availability", "set", "--id", "c", "--date", "2026-10-21", "--status", "maybe").Errors);

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void SetTakesAllFlags(string cancelled)
        => Assert.Empty(Parse("availability", "set", "--id", "c", "--date", "2026-10-21", "--status", "available", "--is-cancelled", cancelled, "--user-id", "u").Errors);

    [Fact]
    public void ToggleCancelHelpSaysItFlips()
        => Assert.Contains("flips", HelpRenderer.Render(CampaignsCommand.Create(), ["campaigns", "availability", "toggle-cancel"], full: false), StringComparison.OrdinalIgnoreCase);
}
