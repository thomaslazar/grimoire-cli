using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class ArmiesCommandTests
{
    private static string RenderHelp(string verb) =>
        HelpRenderer.Render(ArmiesCommand.Create(), ["armies", verb], full: false);

    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        ArmiesCommand.Create().Parse(args);

    [Fact]
    public void ExportRejectsAnUnknownFormat()
        => Assert.NotEmpty(Parse("export", "--id", "a", "--format", "pdf", "--output", "-").Errors);

    [Fact]
    public void ExportNeedsOutput() => Assert.NotEmpty(Parse("export", "--id", "a").Errors);

    [Fact]
    public void ProgressNeedsAUnitId() => Assert.NotEmpty(Parse("progress", "--id", "a").Errors);

    [Fact]
    public void CreateNeedsABodySource() => Assert.NotEmpty(Parse("create").Errors);

    [Fact]
    public void ImportNeedsAnExistingFile() => Assert.NotEmpty(Parse("import", "--file", "/no/such/file.rosz").Errors);

    [Fact]
    public void NoArmiesCommandCarriesARoleTag()
    {
        foreach (var verb in new[] { "list", "get", "create", "update", "delete", "import", "export", "publications", "progress", "games", "factions" })
            Assert.DoesNotContain("Role required:", RenderHelp(verb));
    }

    [Fact]
    public void UpdateWarnsAboutImportedArmies() => Assert.Contains("imported", RenderHelp("update"));

    [Fact]
    public void ProgressWarnsItResets() => Assert.Contains("reset", RenderHelp("progress"));
}
