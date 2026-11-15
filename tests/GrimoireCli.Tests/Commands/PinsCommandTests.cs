using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class PinsCommandTests
{
    private static string RenderHelp(string verb) =>
        HelpRenderer.Render(PinsCommand.Create(), ["pins", verb], full: false);

    private static System.CommandLine.ParseResult Parse(params string[] args) => PinsCommand.Create().Parse(args);

    [Fact]
    public void AddRejectsAnUnknownCollection()
        => Assert.NotEmpty(Parse("add", "--collection", "books", "--target", "x").Errors);

    [Fact]
    public void AddParses()
        => Assert.Empty(Parse("add", "--collection", "systems", "--target", "x").Errors);

    [Fact]
    public void AddNeedsATarget()
        => Assert.NotEmpty(Parse("add", "--collection", "systems").Errors);

    [Fact]
    public void RemoveNeedsAPinId() => Assert.NotEmpty(Parse("remove").Errors);

    [Fact]
    public void NoPinsCommandCarriesARoleTag()
    {
        foreach (var verb in new[] { "list", "add", "remove" })
            Assert.DoesNotContain("Role required:", RenderHelp(verb));
    }

    [Fact]
    public void AddStatesThePinCap() => Assert.Contains("100", RenderHelp("add"));
}
