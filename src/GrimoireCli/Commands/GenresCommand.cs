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
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List all genres (tiered)");
        command.AddExamples("grimoire-cli genres list");
        command.AddResponseExample<Generated.Models.GenresResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new GenresService(client);
            var result = await service.ListAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateCreateCommand()
    {
        var nameOption = new Option<string>("--name") { Description = "The genre's name", Required = true };
        var parentOption = new Option<string?>("--parent-id") { Description = "Nest under this genre, by id from genres list" };
        var command = new Command("create", "Create a genre")
        {
            nameOption, parentOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 when the name already exists, matched case-insensitively.",
            "",
            "Creating a genre does not make it enforced: nothing validates a",
            "system's or book's genres against this list.");
        command.AddExamples(
            "grimoire-cli genres create --name \"Solo\"",
            "grimoire-cli genres create --name \"Solo\" --parent-id <genre-id>");
        command.AddResponseExample<Generated.Models.GenreOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new GenresService(client);
            var result = await service.CreateAsync(
                parseResult.GetValue(nameOption)!,
                parseResult.GetValue(parentOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "The genre's id, from genres list", Required = true };
        var forceOption = new Option<bool>("--force") { Description = "Delete even while the name is in use" };
        var command = new Command("delete", "Delete a genre")
        {
            idOption, forceOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 while the name is in use; the body carries usage_count and name.",
            "",
            "--force strips nothing: removed_usage counts the systems and books",
            "that keep the value, stored as a plain string rather than a",
            "reference; child genres are deleted with the parent.",
            "",
            "Built-in entries are deletable and cannot be restored: the defaults",
            "are seeded once by migration, and create returns a new id with",
            "is_default false.");
        command.AddExamples(
            "grimoire-cli genres delete --id <genre-id>",
            "grimoire-cli genres delete --id <genre-id> --force");
        command.AddResponseExample<Generated.Models.LookupDeleteResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new GenresService(client);
            var result = await service.DeleteAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(forceOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
