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
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
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

    private static Command CreateCreateCommand()
    {
        var nameOption = new Option<string>("--name") { Description = "The system family's name", Required = true };
        var command = new Command("create", "Create a system family")
        {
            nameOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 when the name already exists, matched case-insensitively.",
            "",
            "Creating a value does not make it enforced: nothing validates a",
            "system's system_family against this list.");
        command.AddExamples("grimoire-cli system-families create --name \"DSA\"");
        command.AddResponseExample<Generated.Models.LookupOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemFamiliesService(client);
            var result = await service.CreateAsync(parseResult.GetValue(nameOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "The system family's id, from system-families list", Required = true };
        var forceOption = new Option<bool>("--force") { Description = "Delete even while the name is in use" };
        var command = new Command("delete", "Delete a system family")
        {
            idOption, forceOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 while the name is in use; the body carries usage_count and name.",
            "",
            "--force strips nothing: removed_usage counts the systems that",
            "keep the value, stored as a plain string rather than a reference.",
            "",
            "Built-in entries are deletable and cannot be restored: the defaults",
            "are seeded once by migration, and create returns a new id with",
            "is_default false.");
        command.AddExamples(
            "grimoire-cli system-families delete --id <family-id>",
            "grimoire-cli system-families delete --id <family-id> --force");
        command.AddResponseExample<Generated.Models.LookupDeleteResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemFamiliesService(client);
            var result = await service.DeleteAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(forceOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
