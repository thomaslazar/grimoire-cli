using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

/// <summary>
/// AddRoleRequired is currently unused by any real command — every endpoint the
/// CLI calls today (login, config, systems list/get, self-test) is readable by
/// any authenticated non-guest role, so none qualifies under CLAUDE.md's rule
/// that it's reserved for endpoints needing a non-default role. These tests
/// exercise the mechanism directly on a throwaway command, the way abs-cli's
/// PermissionSectionTests exercises its equivalent, rather than bolting it onto
/// a command that doesn't need it.
/// </summary>
public class RoleSectionTests
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

    [Fact]
    public void AddRoleRequired_RendersRoleRequiredSection_WithGivenRole()
    {
        var cmd = new Command("demo", "Demo command");
        cmd.AddRoleRequired("gm or admin");
        var output = RenderHelp(cmd);
        Assert.Contains("Role required:", output);
        Assert.Contains("gm or admin", output);
    }

    [Fact]
    public void AddRoleRequired_RendersInTopPosition_BeforeOptions()
    {
        var cmd = new Command("demo", "Demo command");
        cmd.AddRoleRequired("gm or admin");
        var output = RenderHelp(cmd);
        var roleIdx = output.IndexOf("Role required:", StringComparison.Ordinal);
        var optionsIdx = output.IndexOf("Options:", StringComparison.Ordinal);
        Assert.True(roleIdx >= 0, "Role required section missing");
        Assert.True(optionsIdx >= 0, "Options section missing");
        Assert.True(roleIdx < optionsIdx, "Role required must render before Options (Top position)");
    }

    [Fact]
    public void CommandWithoutAddRoleRequired_RendersNoRoleSection()
    {
        var cmd = new Command("demo", "Demo command");
        var output = RenderHelp(cmd);
        Assert.DoesNotContain("Role required:", output);
    }

    [Fact]
    public void SystemsListCommand_HasNoRoleRequiredSection()
    {
        var output = RenderHelp(SystemsCommand.Create());
        Assert.DoesNotContain("Role required:", output);
    }
}
