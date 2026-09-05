using System.CommandLine;
using GrimoireCli.Commands;
using GrimoireCli.Output;

var _logger = NLog.LogManager.GetLogger("GrimoireCli.Program");

// Passthrough writes the server's own characters straight to Console.Out, unlike
// the old serialize-through-System.Text.Json path, which escaped every non-ASCII
// character and so was ASCII-safe regardless of console encoding. Without this,
// Windows' default console output code page can mangle non-ASCII bytes (this
// repo's fixtures are German) into '?' or the wrong characters. Some redirected
// hosts (e.g. certain CI runners) throw on the setter, and a broken stdout
// encoding must not take the whole command down with it.
try
{
    Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
catch (IOException)
{
}

var rootCommand = new RootCommand("grimoire-cli — Grimoire TTRPG library CLI");

// Recursive so the flag works where a caller naturally writes it — after the
// subcommand. Without it the token parses as an unmatched option, the command
// prints usage and exits, which reads as a broken flag rather than a misplaced one.
var debugOption = new Option<bool>("--debug")
{
    Description = "Enable debug-level logging (HTTP requests, token expiry, version check) to stderr.",
    Recursive = true
};
var logJsonOption = new Option<bool>("--log-json")
{
    Description = "Emit stderr log lines as single-line JSON instead of text.",
    Recursive = true
};
var prettyOption = new Option<bool>("--pretty")
{
    Description = "Indent API response bodies. Off by default — responses are the server's bytes, "
        + "unmodified. config get and --output receipts are always indented, regardless of this flag.",
    Recursive = true
};
rootCommand.Options.Add(debugOption);
rootCommand.Options.Add(logJsonOption);
rootCommand.Options.Add(prettyOption);

rootCommand.Subcommands.Add(LoginCommand.Create());
rootCommand.Subcommands.Add(MeCommand.Create());
rootCommand.Subcommands.Add(ConfigCommand.Create());
rootCommand.Subcommands.Add(SystemsCommand.Create());
rootCommand.Subcommands.Add(BooksCommand.Create());
rootCommand.Subcommands.Add(LibraryCommand.Create());
rootCommand.Subcommands.Add(AddonsCommand.Create());
rootCommand.Subcommands.Add(BackupsCommand.Create());
rootCommand.Subcommands.Add(FilesCommand.Create());
rootCommand.Subcommands.Add(GenresCommand.Create());
rootCommand.Subcommands.Add(LicensesCommand.Create());
rootCommand.Subcommands.Add(ParentSystemsCommand.Create());
rootCommand.Subcommands.Add(SystemFamiliesCommand.Create());
rootCommand.Subcommands.Add(DiceMaterialsCommand.Create());
rootCommand.Subcommands.Add(SelfTestCommand.Create());

rootCommand.AddHelpSection("Environment variables",
    "GRIMOIRE_SERVER   Server URL, overriding the config file.",
    "GRIMOIRE_DEBUG=1  Same as --debug. Enables debug-level logging to stderr.");

rootCommand.UseCustomHelpSections();

var parseResult = rootCommand.Parse(args);
var debugEnabled = parseResult.GetValue(debugOption)
                   || Environment.GetEnvironmentVariable("GRIMOIRE_DEBUG") == "1";
var logJson = parseResult.GetValue(logJsonOption);
LogSetup.Configure(debugEnabled, logJson);
ConsoleOutput.Pretty = parseResult.GetValue(prettyOption);

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
