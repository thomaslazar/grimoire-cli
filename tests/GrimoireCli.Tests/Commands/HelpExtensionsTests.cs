using System.CommandLine;
using GrimoireCli.Commands;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Commands;

/// <summary>
/// Unit-level coverage of HelpExtensions itself, using throwaway "demo" commands
/// rather than the real command tree — HelpOutputTests covers the real tree.
/// </summary>
public class HelpExtensionsTests
{
    private static string RenderHelp(Command command)
    {
        var root = new RootCommand { command };
        root.UseCustomHelpSections();
        var output = new StringWriter();
        var config = new InvocationConfiguration { Output = output };
        root.Parse(new[] { command.Name, "--help" }).Invoke(config);
        return output.ToString();
    }

    private static string RenderHelpFull(Command command)
    {
        var root = new RootCommand { command };
        root.UseCustomHelpSections();
        var output = new StringWriter();
        var config = new InvocationConfiguration { Output = output };
        root.Parse(new[] { command.Name, "--help-full" }).Invoke(config);
        return output.ToString();
    }

    [Fact]
    public void TopSection_RendersBeforeOptions()
    {
        var cmd = new Command("demo", "Demo command");
        cmd.AddHelpSection("Notes", HelpSectionPosition.Top, "Top-placed content");
        var output = RenderHelp(cmd);
        var notesIdx = output.IndexOf("Notes:", StringComparison.Ordinal);
        var optionsIdx = output.IndexOf("Options:", StringComparison.Ordinal);
        Assert.True(notesIdx >= 0, "Notes section missing");
        Assert.True(optionsIdx >= 0, "Options section missing");
        Assert.True(notesIdx < optionsIdx, "Notes should render before Options");
    }

    [Fact]
    public void BottomSection_RendersAfterOptions()
    {
        var cmd = new Command("demo", "Demo command");
        cmd.AddHelpSection("Examples", HelpSectionPosition.Bottom, "grimoire-cli demo");
        var output = RenderHelp(cmd);
        var examplesIdx = output.IndexOf("Examples:", StringComparison.Ordinal);
        var optionsIdx = output.IndexOf("Options:", StringComparison.Ordinal);
        Assert.True(examplesIdx > optionsIdx, "Examples should render after Options");
    }

    [Fact]
    public void ExistingOverload_DefaultsToBottom()
    {
        var cmd = new Command("demo", "Demo command");
        cmd.AddHelpSection("Examples", "grimoire-cli demo");
        var output = RenderHelp(cmd);
        var examplesIdx = output.IndexOf("Examples:", StringComparison.Ordinal);
        var optionsIdx = output.IndexOf("Options:", StringComparison.Ordinal);
        Assert.True(examplesIdx > optionsIdx, "Default overload must remain Bottom-placed");
    }

    [Fact]
    public void AddResponseExample_RendersResponseShapeSection_AsObject()
    {
        var cmd = new Command("demo", "Demo");
        cmd.AddResponseExample<GameSystemSummary>();
        var output = RenderHelpFull(cmd);
        Assert.Contains("Response shape:\n  {", output);
        Assert.Contains("\"book_count\": 227", output);
    }

    [Fact]
    public void AddResponseExampleArray_WrapsSampleInBrackets()
    {
        var cmd = new Command("demo", "Demo");
        cmd.AddResponseExampleArray<GameSystemSummary>();
        var output = RenderHelpFull(cmd);
        Assert.Contains("Response shape:\n  [", output);
        Assert.Contains("\"book_count\": 227", output);
        var openIdx = output.IndexOf("Response shape:\n  [", StringComparison.Ordinal);
        var closeIdx = output.IndexOf("\n  ]", StringComparison.Ordinal);
        Assert.True(closeIdx > openIdx, "Array wrapper must close after it opens");
    }

    [Fact]
    public void PlainHelp_HidesShapeSection_AndShowsHint()
    {
        var cmd = new Command("demo", "Demo");
        cmd.AddResponseExample<GameSystemSummary>();
        var output = RenderHelp(cmd);
        Assert.DoesNotContain("Response shape:", output);
        Assert.Contains("Run --help-full to see response shape(s).", output);
    }

    [Fact]
    public void HelpFull_ShowsShape_AndOmitsHint()
    {
        var cmd = new Command("demo", "Demo");
        cmd.AddResponseExample<GameSystemSummary>();
        var output = RenderHelpFull(cmd);
        Assert.Contains("Response shape:", output);
        Assert.DoesNotContain("Run --help-full", output);
    }

    [Fact]
    public void PlainHelp_NoShapeSection_OmitsHint()
    {
        var cmd = new Command("demo", "Demo");
        cmd.AddHelpSection("Examples", "grimoire-cli demo");
        var output = RenderHelp(cmd);
        Assert.DoesNotContain("Run --help-full", output);
    }

    [Fact]
    public void GetExampleCount_ReflectsRegisteredExamples()
    {
        var cmd = new Command("demo", "Demo command");
        Assert.Equal(0, cmd.GetExampleCount());
        cmd.AddExamples("grimoire-cli demo one", "grimoire-cli demo two");
        Assert.Equal(2, cmd.GetExampleCount());
        cmd.AddExamples("grimoire-cli demo three");
        Assert.Equal(3, cmd.GetExampleCount());
    }
}
