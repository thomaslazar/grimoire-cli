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
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List all parent systems");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Ships empty: Grimoire seeds no defaults, and a container child's",
            "parent_system is folder-derived, so a value in use need not appear here.");
        command.AddExamples("grimoire-cli parent-systems list");
        command.AddResponseExample<Generated.Models.ParentSystemsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new ParentSystemsService(client);
            var result = await service.ListAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateCreateCommand()
    {
        var nameOption = new Option<string>("--name") { Description = "The parent system's name", Required = true };
        var command = new Command("create", "Create a parent system")
        {
            nameOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 when the name already exists, matched case-insensitively.",
            "",
            "Creating a value does not make it enforced: nothing validates a",
            "system's parent_system against this list.");
        command.AddExamples("grimoire-cli parent-systems create --name \"D20\"");
        command.AddResponseExample<Generated.Models.LookupOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new ParentSystemsService(client);
            var result = await service.CreateAsync(parseResult.GetValue(nameOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "The parent system's id, from parent-systems list", Required = true };
        var forceOption = new Option<bool>("--force") { Description = "Delete even while the name is in use" };
        var command = new Command("delete", "Delete a parent system")
        {
            idOption, forceOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 while the name is in use; the body carries usage_count and name.",
            "",
            "--force strips nothing: removed_usage counts the systems that",
            "keep the value, stored as a plain string rather than a reference.");
        command.AddExamples(
            "grimoire-cli parent-systems delete --id <parent-id>",
            "grimoire-cli parent-systems delete --id <parent-id> --force");
        command.AddResponseExample<Generated.Models.LookupDeleteResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new ParentSystemsService(client);
            var result = await service.DeleteAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(forceOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
