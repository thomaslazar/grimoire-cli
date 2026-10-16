using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignMemberSheetCommandTests
{
    private static System.CommandLine.ParseResult Parse(params string[] args) => CampaignsCommand.Create().Parse(["members", "sheet", .. args]);

    [Fact]
    public void GroupHoldsExactlyTheFiveCommands()
    {
        var sheet = CampaignsCommand.Create().Subcommands.Single(c => c.Name == "members").Subcommands.Single(c => c.Name == "sheet");
        Assert.Equal(["get", "upload", "delete", "from-source", "sources"], sheet.Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void FromSourceRejectsUnknownType() => Assert.NotEmpty(Parse("from-source", "--id", "c", "--member-id", "m", "--source-type", "pdf", "--source-id", "b").Errors);

    [Fact]
    public void FromSourceParses() => Assert.Empty(Parse("from-source", "--id", "c", "--member-id", "m", "--source-type", "book", "--source-id", "b").Errors);

    [Fact]
    public void SourcesParsesWithoutMemberId() => Assert.Empty(Parse("sources", "--id", "c").Errors);

    [Fact]
    public void SourcesHasNoMemberId()
        => Assert.NotEmpty(Parse("sources", "--id", "c", "--member-id", "m").Errors);

    [Fact]
    public void GetNeedsOutput() => Assert.NotEmpty(Parse("get", "--id", "c", "--member-id", "m").Errors);
}
