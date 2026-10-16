using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignMemberTokenCommandTests
{
    private static System.CommandLine.ParseResult Parse(params string[] args) => CampaignsCommand.Create().Parse(["members", "token", .. args]);

    [Fact]
    public void GroupHoldsExactlyTheThreeCommands()
    {
        var token = CampaignsCommand.Create().Subcommands.Single(c => c.Name == "members").Subcommands.Single(c => c.Name == "token");
        Assert.Equal(["get", "upload", "delete"], token.Subcommands.Select(c => c.Name));
    }

    [Theory]
    [InlineData("get", "--output", "-")]
    [InlineData("upload", "--file", "f")]
    [InlineData("delete")]
    public void EveryCommandNeedsMemberId(params string[] rest)
    {
        Assert.NotEmpty(Parse([rest[0], "--id", "c", .. rest[1..]]).Errors);
        Assert.Empty(Parse([rest[0], "--id", "c", "--member-id", "m", .. rest[1..]]).Errors);
    }

    [Fact]
    public void GetNeedsOutput() => Assert.NotEmpty(Parse("get", "--id", "c", "--member-id", "m").Errors);

    [Fact]
    public void UploadNeedsFile() => Assert.NotEmpty(Parse("upload", "--id", "c", "--member-id", "m").Errors);
}
