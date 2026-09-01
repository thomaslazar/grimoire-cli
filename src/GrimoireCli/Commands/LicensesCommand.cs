using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class LicensesCommand
{
    public static Command Create()
    {
        var command = new Command("licenses", "The license vocabulary");
        command.Subcommands.Add(CreateListCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("list", "List all licenses") { serverOption };
        command.AddExamples("grimoire-cli licenses list");
        command.AddResponseExample<Generated.Models.LicensesResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new LicensesService(client);
            var result = await service.ListAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
