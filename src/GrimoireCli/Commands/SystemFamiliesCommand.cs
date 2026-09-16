using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class SystemFamiliesCommand
{
    public static Command Create()
    {
        var command = new Command("system-families", "The system-family vocabulary");
        command.Subcommands.Add(CreateListCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List all system families");
        command.AddExamples("grimoire-cli system-families list");
        command.AddResponseExample<Generated.Models.SystemFamiliesResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemFamiliesService(client);
            var result = await service.ListAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
