using System.CommandLine;
using GrimoireCli.Commands;
using GrimoireCli.Output;

var _logger = NLog.LogManager.GetLogger("GrimoireCli.Program");

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
rootCommand.Subcommands.Add(MeCommand.Create());
rootCommand.Subcommands.Add(ConfigCommand.Create());
rootCommand.Subcommands.Add(SystemsCommand.Create());
rootCommand.Subcommands.Add(BooksCommand.Create());
rootCommand.Subcommands.Add(LibraryCommand.Create());
rootCommand.Subcommands.Add(AddonsCommand.Create());
rootCommand.Subcommands.Add(SelfTestCommand.Create());

rootCommand.AddHelpSection("Environment variables",
    "GRIMOIRE_SERVER   Server URL, overriding the config file.",
    "GRIMOIRE_TOKEN    JWT, overriding the config file.",
    "GRIMOIRE_DEBUG=1  Same as --debug. Enables debug-level logging to stderr.");

rootCommand.UseCustomHelpSections();

var parseResult = rootCommand.Parse(args);
var debugEnabled = parseResult.GetValue(debugOption)
                   || Environment.GetEnvironmentVariable("GRIMOIRE_DEBUG") == "1";
var logJson = parseResult.GetValue(logJsonOption);
LogSetup.Configure(debugEnabled, logJson);

try
{
    return await parseResult.InvokeAsync();
}
catch (Exception ex)
{
    _logger.Error(ex.Message);
    _logger.Debug(ex.ToString());
    return 2;
}
