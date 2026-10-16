using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignMembersCommandTests
{
    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        CampaignsCommand.Create().Parse(args);

    [Fact]
    public void TheGroupHoldsTheMemberCommands()
        => Assert.Equal(new[] { "art", "eligible", "invite", "remove", "sheet", "token", "update" },
            CampaignsCommand.Create().Subcommands.Single(c => c.Name == "members").Subcommands.Select(c => c.Name).Order());

    [Fact]
    public void InvitesIsACampaignsSubcommand()
    {
        Assert.Contains(CampaignsCommand.Create().Subcommands, c => c.Name == "invites");
        Assert.Empty(Parse("invites").Errors);
    }

    [Theory]
    [InlineData("invite")]
    [InlineData("update")]
    [InlineData("remove")]
    public void MemberCommandsNeedAUserId(string verb)
    {
        Assert.NotEmpty(Parse("members", verb, "--id", "c").Errors);
        Assert.Empty(Parse("members", verb, "--id", "c", "--user-id", "u").Errors);
    }

    [Fact]
    public void UpdateRejectsAnUnknownStatus()
        => Assert.NotEmpty(Parse("members", "update", "--id", "c", "--user-id", "u", "--status", "maybe").Errors);

    [Fact]
    public void UpdateTakesAllFlags()
        => Assert.Empty(Parse("members", "update", "--id", "c", "--user-id", "u", "--status", "accepted", "--character-name", "", "--character-sheet-url", "x").Errors);
}
