using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignResourcesCommandTests
{
    private static string RenderHelp(string verb) =>
        HelpRenderer.Render(CampaignsCommand.Create(), ["campaigns", "resources", verb], full: false);

    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        CampaignsCommand.Create().Parse(["resources", .. args]);

    [Theory]
    [InlineData("{\"resources\":[{},{}]}", "[{}]", true)]
    [InlineData("{\"resources\":[{},{}]}", "[{},{}]", false)]
    [InlineData("{\"resources\":[]}", "[]", false)]
    public void SkippedAnyComparesSentWithCreated(string body, string response, bool expected)
        => Assert.Equal(expected, CampaignResourcesCommands.SkippedAny(body, response));

    [Fact]
    public void AddRejectsFileAsAResourceType()
        => Assert.NotEmpty(Parse("add", "--id", "c", "--resource-type", "file", "--resource-id", "x").Errors);

    [Fact]
    public void AddRejectsAnUnknownVisibility()
        => Assert.NotEmpty(Parse("add", "--id", "c", "--resource-type", "book", "--resource-id", "x", "--visibility", "players").Errors);

    [Fact]
    public void AddAcceptsAModel()
        => Assert.Empty(Parse("add", "--id", "c", "--resource-type", "model", "--resource-id", "x").Errors);

    [Fact]
    public void UpdateNeedsSomethingToChange()
        => Assert.NotEmpty(Parse("update", "--id", "c", "--link-id", "l").Errors);

    [Fact]
    public void NoResourcesCommandCarriesARoleTag()
    {
        foreach (var verb in new[] { "list", "add", "bulk", "update", "remove", "reorder" })
            Assert.DoesNotContain("Role required:", RenderHelp(verb));
    }

    [Fact]
    public void BulkDocumentsExitThree() => Assert.Contains("Exit 3", RenderHelp("bulk"));

    [Fact]
    public void RemoveWarnsThatAFileLinkDeletesTheUpload() => Assert.Contains("deletes the upload", RenderHelp("remove"));

    [Fact]
    public void ReorderSaysToPassEveryId() => Assert.Contains("every link id", RenderHelp("reorder"));
}
