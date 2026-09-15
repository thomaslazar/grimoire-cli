using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class LogsCommand
{
    private static readonly string[] Levels = ["debug", "info", "warning", "error", "critical"];

    public static Command Create()
    {
        var levelOption = OptionHelpers.Choice("--level", "Minimum level to return; default info", Levels);
        var limitOption = OptionHelpers.Range("--limit", "Entries to return; default 200, max 20000", 1, 20000);
        var offsetOption = OptionHelpers.Range("--offset", "Entries to skip from the newest end", 0);
        var afterSeqOption = OptionHelpers.Range("--after-seq", "Return only entries newer than this seq", 0);
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("logs", "Read the server's application log")
        {
            levelOption, limitOption, offsetOption, afterSeqOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Ring buffer of the last 20000 entries; anything older is gone. DEBUG is",
            "available here whatever the server's LOG_LEVEL is set to.",
            "",
            "--level is a minimum: error returns error and critical.",
            "",
            "A page is taken from the newest end and returned oldest-first. --offset is",
            "ignored when --after-seq is given.",
            "",
            "To poll, pass the previous response's max_seq back as --after-seq. max_seq",
            "tracks the whole buffer rather than the filtered set, so a --level that",
            "matches nothing still advances the cursor.",
            "",
            "total counts what matches --level, not what this page holds.");
        command.AddExamples(
            "grimoire-cli logs --level error",
            "grimoire-cli logs --after-seq 1423 --level warning");
        command.AddResponseExample<Generated.Models.LogsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var result = await new LogsService(client).ReadAsync(
                parseResult.GetValue(levelOption),
                parseResult.GetValue(limitOption),
                parseResult.GetValue(offsetOption),
                parseResult.GetValue(afterSeqOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
