using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

/// <summary>
/// AddRoleRequired's first real call sites are the systems write commands
/// (require_gm_or_admin). These tests exercise the mechanism directly on a
/// throwaway command, the way abs-cli's PermissionSectionTests does, and assert
/// that the commands which need no role carry no tag.
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

    [Theory]
    [InlineData("update")]
    [InlineData("batch-update")]
    [InlineData("batch-tag")]
    [InlineData("book-folders set")]
    [InlineData("book-folders delete")]
    public void SystemsWriteCommandHasTheGmOrAdminRoleSection(string subcommand)
    {
        var root = new RootCommand { SystemsCommand.Create() };
        root.UseCustomHelpSections();
        var output = new StringWriter();
        // Split so a nested group's subcommand ("book-folders set") parses too.
        root.Parse(["systems", .. subcommand.Split(' '), "--help"])
            .Invoke(new InvocationConfiguration { Output = output });
        Assert.Contains("Role required:", output.ToString());
        Assert.Contains("gm or admin", output.ToString());
    }
}
