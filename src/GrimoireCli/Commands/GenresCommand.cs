using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class GenresCommand
{
    public static Command Create()
    {
        var command = new Command("genres", "The genre vocabulary");
        command.Subcommands.Add(CreateListCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("list", "List all genres (tiered)") { serverOption };
        command.AddExamples("grimoire-cli genres list");
        command.AddResponseExample<Generated.Models.GenresResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new LookupsService(client);
            var result = await service.ListAsync("genres");
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
