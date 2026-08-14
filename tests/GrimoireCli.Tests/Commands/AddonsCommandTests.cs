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

    [Fact]
    public void InstallDocumentsTheDigestAndTheScriptConsent()
    {
        var output = RenderHelp(["addons", "install"], full: false);
        Assert.Contains("verified against the index's digest", output);
        Assert.Contains("drops back to unapproved", output);
    }

    [Fact]
    public void UpdateSaysItDoesNotChangeVersion()
    {
        var output = RenderHelp(["addons", "update"], full: false);
        Assert.Contains("never version", output);
    }

    // Both are tri-state: omitted must leave the field alone, so a plain switch
    // could set but never clear.
    [Fact]
    public void UpdateTakesTriStateBooleans()
    {
        var output = RenderHelp(["addons", "update"], full: false);
        Assert.Contains("--enabled", output);
        Assert.Contains("--script-approved", output);
        Assert.Contains("true|false", output.Replace(" ", ""));
    }

    [Fact]
    public void UninstallRegistersNoResponseShape()
    {
        Assert.DoesNotContain("Response shape:", RenderHelp(["addons", "uninstall"], full: true));
        Assert.Contains("{\"status\": \"ok\"}", RenderHelp(["addons", "uninstall"], full: false));
    }

    [Fact]
    public void EveryAddonCommandCarriesTheAdminTag()
    {
        foreach (var verb in new[] { "list", "refresh", "install", "update", "upgrade-all", "uninstall", "settings" })
            Assert.Contains("Role required:\n  admin\n", RenderHelp(["addons", verb], full: false));
    }

    // No add-on body is written by the caller, so none of the seven documents one.
    [Fact]
    public void NoAddonCommandRegistersARequestShape()
    {
        foreach (var verb in new[] { "list", "refresh", "install", "update", "upgrade-all", "uninstall", "settings" })
            Assert.DoesNotContain("Request shape:", RenderHelp(["addons", verb], full: true));
    }

    [Fact]
    public void UpgradeAllDocumentsItsPartialFailure()
    {
        var output = RenderHelp(["addons", "upgrade-all"], full: true);
        Assert.Contains("Exit 3", output);
        Assert.Contains("\"failed\":", output);
        Assert.Contains("not carried over", output);
    }

    [Fact]
    public void SettingsRequiresAFlag()
    {
        var output = RenderHelp(["addons", "settings"], full: false);
        Assert.Contains("At least one flag is required.", output);
        Assert.Contains("does not refetch", output);
    }
}
