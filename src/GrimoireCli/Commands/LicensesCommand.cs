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
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List all licenses");
        command.AddExamples("grimoire-cli licenses list");
        command.AddResponseExample<Generated.Models.LicensesResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new LicensesService(client);
            var result = await service.ListAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateCreateCommand()
    {
        var nameOption = new Option<string>("--name") { Description = "The license's name", Required = true };
        var command = new Command("create", "Create a license")
        {
            nameOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 when the name already exists, matched case-insensitively.",
            "",
            "Creating a value does not make it enforced: nothing validates a",
            "system's or book's license against this list.");
        command.AddExamples("grimoire-cli licenses create --name \"OGL 1.0a\"");
        command.AddResponseExample<Generated.Models.LookupOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new LicensesService(client);
            var result = await service.CreateAsync(parseResult.GetValue(nameOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "The license's id, from licenses list", Required = true };
        var forceOption = new Option<bool>("--force") { Description = "Delete even while the name is in use" };
        var command = new Command("delete", "Delete a license")
        {
            idOption, forceOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 while the name is in use; the body carries usage_count and name.",
            "",
            "--force strips nothing: removed_usage counts the systems and books",
            "that keep the value, stored as a plain string rather than a",
            "reference.",
            "",
            "Built-in entries are deletable and cannot be restored: the defaults",
            "are seeded once by migration, and create returns a new id with",
            "is_default false.");
        command.AddExamples(
            "grimoire-cli licenses delete --id <license-id>",
            "grimoire-cli licenses delete --id <license-id> --force");
        command.AddResponseExample<Generated.Models.LookupDeleteResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new LicensesService(client);
            var result = await service.DeleteAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(forceOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
