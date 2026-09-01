using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class DiceMaterialsCommand
{
    public static Command Create()
    {
        var command = new Command("dice-materials", "The dice/material vocabulary");
        command.Subcommands.Add(CreateListCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("list", "List all dice/materials") { serverOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "group buckets the entry, and is Custom when unset.");
        command.AddExamples("grimoire-cli dice-materials list");
        command.AddResponseExample<Generated.Models.DiceMaterialsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new LookupsService(client);
            var result = await service.ListAsync("dice-materials");
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
