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
