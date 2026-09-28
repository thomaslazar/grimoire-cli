using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class SidecarsCommand
{
    public static Command Create()
    {
        var command = new Command("sidecars", "Read and write metadata sidecar files beside each book");
        command.Subcommands.Add(CreateSettingsCommand());
        return command;
    }

    private static Command CreateSettingsCommand()
    {
        var command = new Command("settings", "Sidecar export configuration");
        command.Subcommands.Add(CreateSettingsGetCommand());
        return command;
    }

    private static Command CreateSettingsGetCommand()
    {
        var command = new Command("get", "Read the sidecar export configuration");
        command.AddRoleRequired("admin");
        command.AddExamples("grimoire-cli sidecars settings get");
        command.AddResponseExample<Generated.Models.SidecarSettings>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SidecarsService(client);
            var result = await service.SettingsAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
