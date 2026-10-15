using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignBannerCommandTests
{
    private static System.CommandLine.ParseResult Parse(params string[] args) => CampaignsCommand.Create().Parse(args);

    [Fact]
    public void BannerHoldsExactlyTheFiveCommands()
    {
        var banner = CampaignsCommand.Create().Subcommands.Single(c => c.Name == "banner");
        Assert.Equal(["get", "upload", "delete", "from-source", "focus"], banner.Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void GetNeedsOutput() => Assert.NotEmpty(Parse("banner", "get", "--id", "c").Errors);

    [Fact]
    public void GetChecksSize()
    {
        Assert.NotEmpty(Parse("banner", "get", "--id", "c", "--output", "-", "--size", "huge").Errors);
        Assert.Empty(Parse("banner", "get", "--id", "c", "--output", "-", "--size", "full").Errors);
    }

    [Fact]
    public void FromSourceChecksType()
    {
        Assert.Empty(Parse("banner", "from-source", "--id", "c", "--source-type", "campaign_file", "--source-id", "x").Errors);
        Assert.NotEmpty(Parse("banner", "from-source", "--id", "c", "--source-type", "pdf", "--source-id", "x").Errors);
        Assert.NotEmpty(Parse("banner", "from-source", "--id", "c", "--source-id", "x").Errors);
    }

    [Fact]
    public void FocusNeedsFocusY() => Assert.NotEmpty(Parse("banner", "focus", "--id", "c").Errors);

    [Fact]
    public void UploadImageNeedsFile() => Assert.NotEmpty(Parse("files", "upload-image", "--id", "c").Errors);

    [Fact]
    public void UploadImageRefusesBothCategoryFlags()
        => Assert.NotEmpty(Parse("files", "upload-image", "--id", "c", "--file", "f", "--category-id", "k", "--new-category-name", "n").Errors);
}
