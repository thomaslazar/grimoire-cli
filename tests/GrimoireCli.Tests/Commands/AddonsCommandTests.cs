using GrimoireCli.Commands;
using Xunit;

namespace GrimoireCli.Tests.Commands;

public class AddonsCommandTests
{
    private static string RenderHelp(string[] path, bool full) => HelpRenderer.Render(AddonsCommand.Create(), path, full);

    [Fact]
    public void ListShowsBothAddonShapes()
    {
        var output = RenderHelp(["addons", "list"], full: true);
        Assert.Contains("\"installed\":", output);
        Assert.Contains("\"available\":", output);
        Assert.Contains("\"blocked_reason\":", output);
        Assert.Contains("\"script_sha256\":", output);
    }

    // runnable is the field that explains an empty metadata-sources list.
    [Fact]
    public void ListExplainsTheBlockedState()
    {
        var output = RenderHelp(["addons", "list"], full: false);
        Assert.Contains("runnable false", output);
        Assert.Contains("blocked_reason", output);
    }

    [Fact]
    public void RefreshShowsItsCount()
    {
        var output = RenderHelp(["addons", "refresh"], full: true);
        Assert.Contains("\"count\":", output);
    }
}
