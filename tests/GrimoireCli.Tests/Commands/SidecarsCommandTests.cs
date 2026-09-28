using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class SidecarsCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(SidecarsCommand.Create(), path, full);

    [Fact]
    public void SettingsGetParsesWithNoArguments()
    {
        Assert.Empty(SidecarsCommand.Create().Parse(["settings", "get"]).Errors);
    }

    // All three routes are require_admin, so all three carry the tag.
    [Fact]
    public void SettingsGetDeclaresTheAdminRole()
    {
        Assert.Contains("Role required:\n  admin\n", Help(["sidecars", "settings", "get"]));
    }

    [Fact]
    public void SettingsGetTakesNoOptions()
    {
        Assert.NotEmpty(
            SidecarsCommand.Create().Parse(["settings", "get", "--formats", "opf"]).Errors);
    }
}
