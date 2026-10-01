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
}
