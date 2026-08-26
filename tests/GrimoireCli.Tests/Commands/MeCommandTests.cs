using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class MeCommandTests
{
    private static string RenderHelp()
    {
        var root = new RootCommand { MeCommand.Create() };
        root.UseCustomHelpSections();
        var output = new StringWriter();
        root.Parse(new[] { "me", "--help" }).Invoke(new InvocationConfiguration { Output = output });
        return output.ToString();
    }

    [Fact]
    public void TakesNoPositionalArguments()
    {
        var root = new RootCommand { MeCommand.Create() };
        Assert.NotEmpty(root.Parse("me extra").Errors);
    }

    [Fact]
    public void AcceptsAServerOverride()
    {
        var root = new RootCommand { MeCommand.Create() };
        Assert.Empty(root.Parse("me --server http://x").Errors);
    }

    // The token comes from the config file alone; --token is not an option here.
    [Fact]
    public void RejectsATokenOverride()
    {
        var root = new RootCommand { MeCommand.Create() };
        Assert.NotEmpty(root.Parse("me --token t").Errors);
    }

    // GET /api/auth/me is Depends(get_current_user) — any authenticated user,
    // so tagging it with a role would be a lie.
    [Fact]
    public void HasNoRoleRequiredSection()
    {
        Assert.DoesNotContain("Role required:", RenderHelp());
    }

    [Fact]
    public void HelpMentionsWhatRoleIsFor()
    {
        Assert.Contains("gm or admin", RenderHelp());
    }
}
