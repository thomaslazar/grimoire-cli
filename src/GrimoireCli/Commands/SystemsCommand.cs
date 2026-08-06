using System.CommandLine;
using GrimoireCli.Api;
using GrimoireCli.Output;

namespace GrimoireCli.Commands;

public static class SystemsCommand
{
    public static Command Create()
    {
        var command = new Command("systems", "Game systems (the folders under books/)");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List all game systems");
        command.AddExamples("grimoire-cli systems list");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await client.GetAsync(ApiEndpoints.Systems));
            return 0;
        });
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var command = new Command("get", "Get one game system, with its books")
        {
            idOption
        };
        command.AddExamples("grimoire-cli systems get --id <system-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var id = parseResult.GetValue(idOption)!;
            var (client, _) = CommandHelper.BuildClient();
            var json = await client.GetAsync(
                ApiEndpoints.System(id),
                notFoundHint: "No system with that ID. List them with: grimoire-cli systems list");
            ConsoleOutput.WriteRawJson(json);
            return 0;
        });
        return command;
    }
}
