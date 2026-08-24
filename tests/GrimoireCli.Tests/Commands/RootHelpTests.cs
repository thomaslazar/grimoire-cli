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
    private static RootCommand BuildRoot(out Option<bool> debugOption, out Option<bool> logJsonOption, out Option<bool> prettyOption)
    {
        var rootCommand = new RootCommand("grimoire-cli — Grimoire TTRPG library CLI");
        debugOption = new Option<bool>("--debug")
        {
            Description = "Enable debug-level logging (HTTP requests, token expiry, version check) to stderr.",
            Recursive = true
        };
        logJsonOption = new Option<bool>("--log-json")
        {
            Description = "Emit stderr log lines as single-line JSON instead of text.",
            Recursive = true
        };
        prettyOption = new Option<bool>("--pretty")
        {
            Description = "Indent JSON output. Off by default — responses are the server's bytes, unmodified.",
            Recursive = true
        };
        rootCommand.Options.Add(debugOption);
        rootCommand.Options.Add(logJsonOption);
        rootCommand.Options.Add(prettyOption);
        rootCommand.Subcommands.Add(LoginCommand.Create());
        rootCommand.Subcommands.Add(ConfigCommand.Create());
        rootCommand.Subcommands.Add(SystemsCommand.Create());
        rootCommand.Subcommands.Add(SelfTestCommand.Create());
        rootCommand.AddHelpSection("Environment variables",
            "GRIMOIRE_SERVER   Server URL, overriding the config file.",
            "GRIMOIRE_TOKEN    JWT, overriding the config file.",
            "GRIMOIRE_DEBUG=1  Same as --debug. Enables debug-level logging to stderr.");
        rootCommand.UseCustomHelpSections();
        return rootCommand;
    }

    private static string RenderHelp(params string[] args)
    {
        var rootCommand = BuildRoot(out _, out _, out _);
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

    // Both flags are Recursive in Program.cs, so a caller may write them after the
    // subcommand. Without that they parse as an unmatched token: the command prints
    // usage, drops the flag, and reads as broken rather than misplaced.
    [Theory]
    [InlineData("--debug", "config", "get")]
    [InlineData("config", "get", "--debug")]
    [InlineData("config", "--debug", "get")]
    public void DebugOption_IsAccepted_InAnyPosition(params string[] args)
    {
        var root = BuildRoot(out var debugOption, out _, out _);
        var parsed = root.Parse(args);
        Assert.Empty(parsed.Errors);
        Assert.True(parsed.GetValue(debugOption));
    }

    [Theory]
    [InlineData("--log-json", "config", "get")]
    [InlineData("config", "get", "--log-json")]
    public void LogJsonOption_IsAccepted_InAnyPosition(params string[] args)
    {
        var root = BuildRoot(out _, out var logJsonOption, out _);
        var parsed = root.Parse(args);
        Assert.Empty(parsed.Errors);
        Assert.True(parsed.GetValue(logJsonOption));
    }

    [Theory]
    [InlineData("--pretty", "config", "get")]
    [InlineData("config", "get", "--pretty")]
    public void PrettyOption_IsAccepted_InAnyPosition(params string[] args)
    {
        var root = BuildRoot(out _, out _, out var prettyOption);
        var parsed = root.Parse(args);
        Assert.Empty(parsed.Errors);
        Assert.True(parsed.GetValue(prettyOption));
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
