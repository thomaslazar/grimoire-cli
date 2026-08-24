using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

/// <summary>
/// Exercises the real "systems list" / "systems get" help text end to end:
/// Notes/Examples/Options ordering, the --help vs --help-full split on the
/// response-shape block, and the array-vs-object wrapping of that block.
/// </summary>
public class HelpOutputTests
{
    private static string RenderHelp(bool helpFull, params string[] path)
    {
        var root = new RootCommand();
        root.Subcommands.Add(SystemsCommand.Create());
        root.UseCustomHelpSections();
        var output = new StringWriter();
        var config = new InvocationConfiguration { Output = output };
        var args = path.Concat(new[] { helpFull ? "--help-full" : "--help" }).ToArray();
        root.Parse(args).Invoke(config);
        return output.ToString();
    }

    [Fact]
    public void SystemsList_PlainHelp_ShowsNotesExamplesOptions_InOrder()
    {
        var output = RenderHelp(helpFull: false, "systems", "list");
        var notesIdx = output.IndexOf("Notes:", StringComparison.Ordinal);
        var optionsIdx = output.IndexOf("Options:", StringComparison.Ordinal);
        var examplesIdx = output.IndexOf("Examples:", StringComparison.Ordinal);
        Assert.True(notesIdx >= 0, "Notes section missing");
        Assert.True(optionsIdx >= 0, "Options section missing");
        Assert.True(examplesIdx >= 0, "Examples section missing");
        Assert.True(notesIdx < optionsIdx, "Notes (Top) must render before Options");
        Assert.True(examplesIdx > optionsIdx, "Examples (Bottom) must render after Options");
    }

    [Fact]
    public void SystemsList_PlainHelp_ShowsServerAndTokenOptions()
    {
        var output = RenderHelp(helpFull: false, "systems", "list");
        Assert.Contains("--server", output);
        Assert.Contains("--token", output);
    }

    [Fact]
    public void SystemsList_PlainHelp_HidesShape_AndEndsWithHint()
    {
        var output = RenderHelp(helpFull: false, "systems", "list");
        Assert.DoesNotContain("Response shape:", output);
        Assert.Contains("Run --help-full to see the request and response shapes.", output);
    }

    [Fact]
    public void SystemsList_PlainHelp_DocumentsThatChildrenAreHiddenByDefault()
    {
        var output = RenderHelp(helpFull: false, "systems", "list");
        Assert.Contains("--include-children", output);
        Assert.Contains("hidden", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SystemsList_HelpFull_ShowsResponseShapeAsArray_AndOmitsHint()
    {
        var output = RenderHelp(helpFull: true, "systems", "list");
        Assert.Contains("Response shape:\n  [", output);
        Assert.Contains("\"book_count\": 0", output);
        Assert.DoesNotContain("Run --help-full", output);
    }

    [Fact]
    public void SystemsGet_PlainHelp_ShowsNotesExamplesOptions_InOrder()
    {
        var output = RenderHelp(helpFull: false, "systems", "get");
        var notesIdx = output.IndexOf("Notes:", StringComparison.Ordinal);
        var optionsIdx = output.IndexOf("Options:", StringComparison.Ordinal);
        var examplesIdx = output.IndexOf("Examples:", StringComparison.Ordinal);
        Assert.True(notesIdx >= 0 && notesIdx < optionsIdx, "Notes (Top) must render before Options");
        Assert.True(examplesIdx > optionsIdx, "Examples (Bottom) must render after Options");
    }

    [Fact]
    public void SystemsGet_PlainHelp_ShowsServerAndTokenOptions()
    {
        var output = RenderHelp(helpFull: false, "systems", "get");
        Assert.Contains("--server", output);
        Assert.Contains("--token", output);
    }

    [Fact]
    public void SystemsGet_PlainHelp_HidesShape_AndEndsWithHint()
    {
        var output = RenderHelp(helpFull: false, "systems", "get");
        Assert.DoesNotContain("Response shape:", output);
        Assert.Contains("Run --help-full to see the request and response shapes.", output);
    }

    [Fact]
    public void SystemsGet_HelpFull_ShowsResponseShapeAsObject_WithBooks_AndOmitsHint()
    {
        var output = RenderHelp(helpFull: true, "systems", "get");
        Assert.Contains("Response shape:\n  {", output);
        Assert.Contains("\"books\"", output);
        Assert.DoesNotContain("Run --help-full", output);
    }
}
