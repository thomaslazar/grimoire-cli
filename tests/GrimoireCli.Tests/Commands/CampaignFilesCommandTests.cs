using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignFilesCommandTests
{
    private static string RenderHelp() =>
        HelpRenderer.Render(CampaignsCommand.Create(), ["campaigns", "files", "upload"], full: false);

    [Fact]
    public void UploadRefusesBothCategoryFlags()
        => Assert.NotEmpty(CampaignsCommand.Create().Parse(
            ["files", "upload", "--id", "c", "--file", "f", "--category-id", "k", "--new-category-name", "n"]).Errors);

    [Fact]
    public void UploadCarriesNoRoleTag() => Assert.DoesNotContain("Role required:", RenderHelp());

    [Fact]
    public void UploadSaysItLinksAtGm() => Assert.Contains("gm", RenderHelp());

    private static System.CommandLine.ParseResult Parse(params string[] args) => CampaignsCommand.Create().Parse(args);

    [Fact]
    public void FilesHoldsExactlyTheThreeCommands()
    {
        var files = CampaignsCommand.Create().Subcommands.Single(c => c.Name == "files");
        Assert.Equal(["get", "upload", "upload-image"], files.Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void GetCarriesNoRoleTag()
        => Assert.DoesNotContain("Role required:", HelpRenderer.Render(CampaignsCommand.Create(), ["campaigns", "files", "get"], full: false));

    [Fact]
    public void UploadImageNeedsFile() => Assert.NotEmpty(Parse("files", "upload-image", "--id", "c").Errors);

    [Fact]
    public void UploadImageRefusesBothCategoryFlags()
        => Assert.NotEmpty(Parse("files", "upload-image", "--id", "c", "--file", "f", "--category-id", "k", "--new-category-name", "n").Errors);

    [Fact]
    public void GetNeedsFileIdAndOutput()
    {
        Assert.NotEmpty(Parse("files", "get", "--id", "c", "--output", "-").Errors);
        Assert.NotEmpty(Parse("files", "get", "--id", "c", "--file-id", "f").Errors);
        Assert.Empty(Parse("files", "get", "--id", "c", "--file-id", "f", "--output", "-").Errors);
    }
}
