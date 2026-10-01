using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignCategoriesCommandTests
{
    private static string RenderHelp(string verb) =>
        HelpRenderer.Render(CampaignsCommand.Create(), ["campaigns", "categories", verb], full: false);

    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        CampaignsCommand.Create().Parse(["categories", .. args]);

    [Fact]
    public void DeleteRejectsAnUnknownMode()
        => Assert.NotEmpty(Parse("delete", "--id", "c", "--category-id", "k", "--mode", "purge").Errors);

    [Fact]
    public void UpdateNeedsSomethingToChange()
        => Assert.NotEmpty(Parse("update", "--id", "c", "--category-id", "k").Errors);

    [Fact]
    public void NoCategoriesCommandCarriesARoleTag()
    {
        foreach (var verb in new[] { "list", "create", "update", "delete", "reorder", "group-order" })
            Assert.DoesNotContain("Role required:", RenderHelp(verb));
    }

    [Fact]
    public void GroupOrderTeachesItsKeys()
    {
        var help = RenderHelp("group-order");
        Assert.Contains("cat:<category-id>", help);
        Assert.Contains("type:model", help);
    }

    [Fact]
    public void DeleteWarnsAboutDeleteItems() => Assert.Contains("orphans", RenderHelp("delete"));
}
