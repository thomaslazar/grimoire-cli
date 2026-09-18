using System.CommandLine;
using GrimoireCli.Api;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class MapsCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("maps", "Read and edit map metadata");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateThumbnailCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateBatchUpdateCommand());
        command.Subcommands.Add(CreateBatchTagCommand());
        command.Subcommands.Add(MapFolderCommands.Create());
        return command;
    }

    private static Command CreateListCommand()
    {
        var mapTypeOption = new Option<string?>("--map-type") { Description = "Filter by map type" };
        var folderOption = new Option<string?>("--folder") { Description = "Filter by exact folder path" };
        var limitOption = OptionHelpers.Range("--limit", "Results per page (the server sets no maximum)", 1);
        limitOption.DefaultValueFactory = _ => 100;
        var offsetOption = OptionHelpers.Range("--offset", "Items to skip", 0);
        var command = new Command("list", "List maps")
        {
            mapTypeOption, folderOption, limitOption, offsetOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--folder is an exact folder, not a subtree: battlemaps excludes",
            "battlemaps/caves. Its value is folder_path from maps get, not",
            "relative_path — the maps/ collection root is stripped from folder_path.",
            "",
            "Variants are hidden — only the main copy of a family is listed.",
            "",
            "Page with --offset against total in the response.");
        command.AddExamples(
            "grimoire-cli maps list",
            "grimoire-cli maps list --folder battlemaps --limit 20",
            "grimoire-cli maps list --map-type battlemap --offset 100");
        command.AddResponseExample<Generated.Models.MapListResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var result = await service.ListAsync(
                parseResult.GetValue(mapTypeOption),
                parseResult.GetValue(folderOption),
                parseResult.GetValue(limitOption),
                parseResult.GetValue(offsetOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Map ID", Required = true };
        var command = new Command("get", "Get one map")
        {
            idOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "grid is what detection found, with its own source; grid_width,",
            "grid_height and grid_px are a manual override. All three null means",
            "detection is in charge.");
        command.AddExamples("grimoire-cli maps get --id <map-id>");
        command.AddResponseExample<Generated.Models.MapDetailResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var result = await service.GetAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateThumbnailCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Map ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var command = new Command("thumbnail", "Download the map's rendered thumbnail")
        {
            idOption, outputOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Pregenerated during a scan; 404 when has_thumbnail is false in maps",
            "list.",
            "",
            "--output - writes the image to stdout; a path writes the file and",
            "prints {path, bytes}.");
        command.AddExamples(
            "grimoire-cli maps thumbnail --id <id> --output tavern.webp",
            "grimoire-cli maps thumbnail --id <id> --output - > tavern.webp");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            await using var stream = await service.ThumbnailAsync(parseResult.GetValue(idOption)!);
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

    private static Command CreateUpdateCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Map ID", Required = true };
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("update", "Update one map's metadata")
        {
            idOption, inputOption, stdinOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Clear description, map_type or grid_size with \"\"; an explicit null",
            "does nothing.",
            "",
            "tags replace the set. To add without removing, use batch-tag.",
            "",
            "grid_width, grid_height and grid_px take 0 to clear the override and",
            "resume detection. batch-update cannot: it drops a 0 silently.",
            "",
            "grid_width and grid_height are 0-1000, grid_px 0-2000; outside that is",
            "a 422.",
            "",
            "Responds {\"status\": \"ok\", \"grid_warning\": …} and echoes no field",
            "values. grid_warning is advisory — the write succeeded either way.",
            "Read back with: grimoire-cli maps get --id <id>");
        command.AddExamples(
            "grimoire-cli maps update --id <id> --input grid.json",
            "echo '{\"grid_px\":70}' | grimoire-cli maps update --id <id> --stdin",
            "echo '{\"grid_px\":0}' | grimoire-cli maps update --id <id> --stdin");
        command.AddRequestShape<Generated.Models.MapUpdate>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.MapUpdate.CreateFromDiscriminatorValue,
                    "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var response = await service.UpdateAsync(parseResult.GetValue(idOption)!, body);
            ConsoleOutput.WriteRawJson(response);
            return 0;
        });
        return command;
    }

    private static Command CreateBatchUpdateCommand()
    {
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("batch-update", "Update many maps in one transaction")
        {
            inputOption, stdinOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "1 to 1000 items. Each item requires id.",
            "",
            "Only an unresolved id lands in errors, and the rest apply. Exit 3 is",
            "HTTP 200 with a non-empty errors list — a partial write.",
            "",
            "Nothing else is per-item: a schema-invalid item 422s the whole batch",
            "and nothing is written. grid_width and grid_height are 0-1000, grid_px",
            "0-2000, and no tag may contain / or \\.",
            "",
            "A grid field sent as 0 is dropped here, so this cannot clear an",
            "override — maps update can.");
        command.AddExamples(
            "grimoire-cli maps batch-update --input items.json",
            "jq -c '{items: .}' edits.json | grimoire-cli maps batch-update --stdin");
        command.AddRequestShape<Generated.Models.MapBulkUpdate>();
        command.AddResponseExample<Generated.Models.BulkResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.MapBulkUpdate.CreateFromDiscriminatorValue,
                    "pass each id inside items");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var result = await service.BatchUpdateAsync(body);
            ConsoleOutput.WriteRawJson(result);
            return BulkExit.CodeFor(GrimoireApiClient.HasItems(result, "errors"));
        });
        return command;
    }

    private static Command CreateBatchTagCommand()
    {
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("batch-tag", "Add tags to many maps, additively")
        {
            inputOption, stdinOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "1 to 1000 ids, and at least one tag; an empty list either side is a 422.",
            "",
            "Additive — it never removes a tag. maps update replaces the set.",
            "",
            "Only an unresolved id lands in errors. Exit 3 is HTTP 200 with a",
            "non-empty errors list — a partial write. A tag containing / or \\ is a",
            "422 on the whole request, which writes nothing.");
        command.AddExamples(
            "grimoire-cli maps batch-tag --input tags.json",
            "echo '{\"ids\":[\"<id>\"],\"tags\":[\"dungeon\"]}' | grimoire-cli maps batch-tag --stdin");
        command.AddRequestShape<Generated.Models.BulkAddTags>();
        command.AddResponseExample<Generated.Models.BulkTagResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.BulkAddTags.CreateFromDiscriminatorValue,
                    "pass each id inside ids");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var result = await service.BatchTagAsync(body);
            ConsoleOutput.WriteRawJson(result);
            return BulkExit.CodeFor(GrimoireApiClient.HasItems(result, "errors"));
        });
        return command;
    }
}
