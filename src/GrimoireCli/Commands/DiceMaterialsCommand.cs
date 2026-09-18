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
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List all dice/materials");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "group is Custom when unset.");
        command.AddExamples("grimoire-cli dice-materials list");
        command.AddResponseExample<Generated.Models.DiceMaterialsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new DiceMaterialsService(client);
            var result = await service.ListAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateCreateCommand()
    {
        var nameOption = new Option<string>("--name") { Description = "The dice/material's name", Required = true };
        var groupOption = new Option<string?>("--group") { Description = "Picker grouping label; the server uses Custom when omitted" };
        var command = new Command("create", "Create a dice/material")
        {
            nameOption, groupOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 when the name already exists, matched case-insensitively.",
            "",
            "Creating a value does not make it enforced: nothing validates a",
            "system's dice_materials against this list.");
        command.AddExamples(
            "grimoire-cli dice-materials create --name \"Oak\"",
            "grimoire-cli dice-materials create --name \"Oak\" --group \"Wood\"");
        command.AddResponseExample<Generated.Models.DiceMaterialOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new DiceMaterialsService(client);
            var result = await service.CreateAsync(
                parseResult.GetValue(nameOption)!,
                parseResult.GetValue(groupOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "The dice/material's id, from dice-materials list", Required = true };
        var forceOption = new Option<bool>("--force") { Description = "Delete even while the name is in use" };
        var command = new Command("delete", "Delete a dice/material")
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
            "grimoire-cli dice-materials delete --id <material-id>",
            "grimoire-cli dice-materials delete --id <material-id> --force");
        command.AddResponseExample<Generated.Models.LookupDeleteResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new DiceMaterialsService(client);
            var result = await service.DeleteAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(forceOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
