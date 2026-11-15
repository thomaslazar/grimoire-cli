using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class ArmiesCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("armies", "Wargame army lists");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateImportCommand());
        command.Subcommands.Add(CreateExportCommand());
        command.Subcommands.Add(CreatePublicationsCommand());
        command.Subcommands.Add(CreateProgressCommand());
        command.Subcommands.Add(CreateGamesCommand());
        command.Subcommands.Add(CreateFactionsCommand());
        command.Subcommands.Add(ArmyPublicationLinksCommands.Create());
        return command;
    }

    private static Option<string> ArmyIdOption() =>
        new("--id") { Description = "Army ID", Required = true };

    private static readonly string[] SystemFaction =
    [
        "game_system_id must be a visible wargame system, else 400; faction_id must",
        "belong to it, else 400. A faction given without a system sets the system.",
        "\"\" clears either field. At most 500 units, each with a name; a unit id is",
        "kept when given.",
    ];

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List your armies");
        command.AddHelpSection("Notes", HelpSectionPosition.Top, "Your own armies only, admins included.");
        command.AddExamples("grimoire-cli armies list");
        command.AddResponseExampleArray<Generated.Models.ArmySummary>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new ArmiesService(client).ListAsync());
            return 0;
        });
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = ArmyIdOption();
        var command = new Command("get", "Get one army with its units") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Visible to the owner, an admin and the members of a league where it is a",
            "member's registered army; anyone else gets 404.");
        command.AddExamples("grimoire-cli armies get --id <army-id>");
        command.AddResponseExample<Generated.Models.ArmyDetail>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new ArmiesService(client).GetAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateCreateCommand()
    {
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("create", "Create a hand-built army") { inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top, SystemFaction);
        command.AddExamples(
            "echo '{\"name\":\"Iron Warriors\",\"points_limit\":1000,\"units\":[{\"id\":\"u1\",\"name\":\"Warsmith\",\"points\":120}]}' | grimoire-cli armies create --stdin");
        command.AddRequestShape<Generated.Models.ArmyCreate>();
        command.AddResponseExample<Generated.Models.ArmyDetail>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.ArmyCreate.CreateFromDiscriminatorValue, "it is assigned by the server");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new ArmiesService(client).CreateAsync(body));
            return 0;
        });
        return command;
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = ArmyIdOption();
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("update", "Update an army") { idOption, inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. SystemFaction, "",
            "Owner or admin only. Partial: send only the fields to change. units",
            "replaces a hand-built army's whole list; keep unit ids to keep their",
            "progress. units on an imported army is a 400. Changing the system without",
            "a faction clears the faction."]);
        command.AddExamples(
            "echo '{\"name\":\"Iron Warriors, 2nd list\"}' | grimoire-cli armies update --id <army-id> --stdin");
        command.AddRequestShape<Generated.Models.ArmyUpdate>();
        command.AddResponseExample<Generated.Models.ArmyDetail>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.ArmyUpdate.CreateFromDiscriminatorValue, "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new ArmiesService(client).UpdateAsync(parseResult.GetValue(idOption)!, body));
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = ArmyIdOption();
        var command = new Command("delete", "Delete an army") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Owner or admin only. Un-registers the army from leagues and blanks it in",
            "past games; the games stay.");
        command.AddExamples("grimoire-cli armies delete --id <army-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new ArmiesService(client).DeleteAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateImportCommand()
    {
        var fileOption = new Option<string>("--file") { Description = "Local .ros or .rosz roster file", Required = true };
        fileOption.Validators.Add(result =>
        {
            var path = result.GetValueOrDefault<string>();
            if (path is not null && !File.Exists(path))
                result.AddError($"File not found: {path}");
        });
        var nameOption = new Option<string?>("--name") { Description = "Army name" };
        var command = new Command("import", "Import a BattleScribe or New Recruit roster") { fileOption, nameOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Takes a .ros or .rosz file, detected by content. Over 20 MiB is a 413; a",
            "file that isn't a roster is a 400. System and faction are matched by name",
            "where they can be, otherwise left null.");
        command.AddExamples("grimoire-cli armies import --file list.rosz --name \"Iron Warriors\"");
        command.AddResponseExample<Generated.Models.ArmyDetail>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            string result;
            try
            {
                result = await new ArmiesService(client).ImportAsync(
                    parseResult.GetValue(fileOption)!, parseResult.GetValue(nameOption));
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateExportCommand()
    {
        var idOption = ArmyIdOption();
        var formatOption = OptionHelpers.Choice("--format", "Export shape; default text", ["text", "roster"]);
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for the bytes to stdout",
            Required = true,
        };
        var command = new Command("export", "Export an army") { idOption, formatOption, outputOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "roster returns the imported file unchanged; 404 for a hand-built army.",
            "",
            "--output - writes the bytes to stdout; a path writes the file and prints",
            "{path, bytes}.");
        command.AddExamples(
            "grimoire-cli armies export --id <army-id> --output army.txt",
            "grimoire-cli armies export --id <army-id> --format roster --output - > list.rosz");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            await using var stream = await new ArmiesService(client).ExportAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(formatOption));
            try
            {
                await ConsoleOutput.WriteStreamAsync(stream, parseResult.GetValue(outputOption)!);
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            return 0;
        });
        return command;
    }

    private static Command CreatePublicationsCommand()
    {
        var idOption = ArmyIdOption();
        var command = new Command("publications", "List the publications an army cites") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "A league guest always gets empty suggestions.");
        command.AddExamples("grimoire-cli armies publications --id <army-id>");
        command.AddResponseExampleArray<Generated.Models.ArmyPublication>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new ArmiesService(client).PublicationsAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateProgressCommand()
    {
        var idOption = ArmyIdOption();
        var unitIdOption = new Option<string>("--unit-id") { Description = "Unit ID", Required = true };
        var experienceOption = new Option<int>("--experience") { Description = "Experience points" };
        var honoursOption = new Option<string[]>("--honours")
        {
            Description = "Honours earned",
            AllowMultipleArgumentsPerToken = true,
        };
        var notesOption = new Option<string?>("--notes") { Description = "Notes on the unit" };
        var command = new Command("progress", "Set a unit's progress") { idOption, unitIdOption, experienceOption, honoursOption, notesOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Owner or admin only. Replaces the unit's whole entry, so an omitted flag",
            "resets to 0, empty or \"\".",
            "",
            "Unit ids are not checked. They are the data.forces[].units[].id values",
            "from armies get, recursively through children and forces.");
        command.AddExamples(
            "grimoire-cli armies progress --id <army-id> --unit-id <unit-id> --experience 6 --honours \"Slayer of Kings\"",
            "grimoire-cli armies progress --id <army-id> --unit-id <unit-id> --honours \"Slayer of Kings\" Blooded");
        command.AddResponseExample<Generated.Models.UnitProgress>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new ArmiesService(client).ProgressAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(unitIdOption)!,
                parseResult.GetValue(experienceOption),
                parseResult.GetValue(honoursOption) ?? [],
                parseResult.GetValue(notesOption)));
            return 0;
        });
        return command;
    }

    private static Command CreateGamesCommand()
    {
        var command = new Command("games", "List the wargame systems an army can use");
        command.AddExamples("grimoire-cli armies games");
        command.AddResponseExampleArray<Generated.Models.ArmyGame>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new ArmiesService(client).GamesAsync());
            return 0;
        });
        return command;
    }

    private static Command CreateFactionsCommand()
    {
        var systemIdOption = new Option<string>("--system-id") { Description = "Wargame system ID", Required = true };
        var command = new Command("factions", "List a wargame system's factions") { systemIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "404 unless the system is a wargame you can choose.");
        command.AddExamples("grimoire-cli armies factions --system-id <system-id>");
        command.AddResponseExample<Generated.Models.ArmyFactionsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new ArmiesService(client).FactionsAsync(parseResult.GetValue(systemIdOption)!));
            return 0;
        });
        return command;
    }
}
