using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class ParentSystemsCommand
{
    public static Command Create()
    {
        var command = new Command("parent-systems", "The parent-system vocabulary");
        command.Subcommands.Add(CreateListCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("list", "List all parent systems") { serverOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Ships empty: Grimoire seeds no defaults, and a container child's",
            "parent_system is folder-derived, so a value in use need not appear here.");
        command.AddExamples("grimoire-cli parent-systems list");
        command.AddResponseExample<Generated.Models.ParentSystemsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new ParentSystemsService(client);
            var result = await service.ListAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
