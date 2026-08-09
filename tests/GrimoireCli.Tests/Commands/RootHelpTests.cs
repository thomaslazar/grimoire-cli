using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

/// <summary>
/// Mirrors the root command wiring in Program.cs (options, subcommands, the
/// Environment variables section) to verify root --help without running the
/// published entry point.
/// </summary>
public class RootHelpTests
{
    private static string RenderHelp(params string[] args)
    {
        var rootCommand = new RootCommand("grimoire-cli — Grimoire TTRPG library CLI");
        var debugOption = new Option<bool>("--debug")
        {
            Description = "Enable debug-level logging (HTTP requests, token expiry, version check) to stderr."
        };
        var logJsonOption = new Option<bool>("--log-json")
        {
            Description = "Emit stderr log lines as single-line JSON instead of text."
        };
        rootCommand.Options.Add(debugOption);
        rootCommand.Options.Add(logJsonOption);
        rootCommand.Subcommands.Add(LoginCommand.Create());
        rootCommand.Subcommands.Add(ConfigCommand.Create());
        rootCommand.Subcommands.Add(SystemsCommand.Create());
        rootCommand.Subcommands.Add(SelfTestCommand.Create());
        rootCommand.AddHelpSection("Environment variables",
            "GRIMOIRE_SERVER   Server URL, overriding the config file.",
            "GRIMOIRE_TOKEN    JWT, overriding the config file.",
            "GRIMOIRE_DEBUG=1  Same as --debug. Enables debug-level logging to stderr.");
        rootCommand.UseCustomHelpSections();

        var output = new StringWriter();
        var config = new InvocationConfiguration { Output = output };
        var actualArgs = args.Concat(new[] { "--help" }).ToArray();
        rootCommand.Parse(actualArgs).Invoke(config);
        return output.ToString();
    }

    [Fact]
    public void Root_Help_Shows_DebugOption_WithDescription()
    {
        var output = RenderHelp();
        Assert.Contains("--debug", output);
        Assert.Contains("Enable debug-level logging", output);
    }

    [Fact]
    public void Root_Help_Shows_LogJsonOption_WithDescription()
    {
        var output = RenderHelp();
        Assert.Contains("--log-json", output);
        Assert.Contains("single-line JSON", output);
    }

    [Fact]
    public void Root_Help_Shows_EnvironmentVariablesSection_WithGrimoireVars()
    {
        var output = RenderHelp();
        Assert.Contains("Environment variables", output);
        Assert.Contains("GRIMOIRE_SERVER", output);
        Assert.Contains("GRIMOIRE_TOKEN", output);
        Assert.Contains("GRIMOIRE_DEBUG=1", output);
    }

    [Fact]
    public void Root_Help_Lists_AllSubcommands()
    {
        var output = RenderHelp();
        var commandsIdx = output.IndexOf("Commands:", StringComparison.Ordinal);
        Assert.True(commandsIdx >= 0, "Commands section missing");
        var commandsBlock = output[commandsIdx..];
        Assert.Contains("login", commandsBlock);
        Assert.Contains("config", commandsBlock);
        Assert.Contains("systems", commandsBlock);
        Assert.Contains("self-test", commandsBlock);
    }

    [Fact]
    public void Root_Help_Shows_Both_Options_Together()
    {
        var output = RenderHelp();
        Assert.Contains("--debug", output);
        Assert.Contains("--log-json", output);
        Assert.Contains("GRIMOIRE_SERVER", output);
    }
}
