using System.CommandLine;
using System.CommandLine.Invocation;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

internal static class HelpRenderer
{
    /// <summary>
    /// Renders a subcommand's help exactly as the CLI would, including the custom
    /// sections, so tests assert on what a user sees rather than on registration.
    /// </summary>
    public static string Render(Command command, string[] path, bool full)
    {
        var root = new RootCommand("test") { command };
        root.UseCustomHelpSections();
        var output = new StringWriter();
        root.Parse([.. path, full ? "--help-full" : "--help"])
            .Invoke(new InvocationConfiguration { Output = output });
        return output.ToString();
    }
}
