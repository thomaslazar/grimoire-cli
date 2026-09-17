using System.CommandLine;
using GrimoireCli.Api;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class ModelsCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("models", "Read and edit 3D model metadata");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateThumbnailCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateBatchUpdateCommand());
        command.Subcommands.Add(CreateBatchTagCommand());
        command.Subcommands.Add(ModelFolderCommands.Create());
        return command;
    }

    private static Command CreateListCommand()
    {
        var limitOption = OptionHelpers.Range("--limit", "Results per page (the server sets no maximum)", 1);
        limitOption.DefaultValueFactory = _ => 100;
        var offsetOption = OptionHelpers.Range("--offset", "Items to skip", 0);
        var command = new Command("list", "List 3D models")
        {
            limitOption, offsetOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Variants are hidden — only the main copy of a family is listed.",
            "",
            "A model with is_presupported and is_unsupported both false is unknown,",
            "not unsupported.",
            "",
            "The account's explicit permission filters the list server-side.",
            "",
            "Page with --offset against total in the response.");
        command.AddExamples(
            "grimoire-cli models list",
            "grimoire-cli models list --limit 20 --offset 100");
        command.AddResponseExample<Generated.Models.Model3DListResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new ModelsService(client);
            var result = await service.ListAsync(
                parseResult.GetValue(limitOption),
                parseResult.GetValue(offsetOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Model ID", Required = true };
        var command = new Command("get", "Get one 3D model")
        {
            idOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "is_presupported and is_unsupported are derived from is_supported, which",
            "models update writes. Both false means unknown, not unsupported.");
        command.AddExamples("grimoire-cli models get --id <model-id>");
        command.AddResponseExample<Generated.Models.Model3DDetailResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new ModelsService(client);
            var result = await service.GetAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateThumbnailCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Model ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var command = new Command("thumbnail", "Download the model's rendered thumbnail")
        {
            idOption, outputOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Rendered from the geometry during a scan. Only .stl renders one, so a",
            "model in any other format is a 404 — as is one whose has_thumbnail is",
            "false in models list.",
            "",
            "--output - writes the image to stdout; a path writes the file and",
            "prints {path, bytes}.");
        command.AddExamples(
            "grimoire-cli models thumbnail --id <id> --output mini.webp",
            "grimoire-cli models thumbnail --id <id> --output - > mini.webp");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new ModelsService(client);
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
        var idOption = new Option<string>("--id") { Description = "Model ID", Required = true };
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("update", "Update one model's metadata")
        {
            idOption, inputOption, stdinOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "tags replace the set. To add without removing, use batch-tag.",
            "",
            "Clear description with \"\"; an explicit null does nothing.",
            "",
            "is_supported takes true and false in either direction; only unknown is",
            "one-way. A null is dropped, so a model can leave unknown but never",
            "return to it.",
            "",
            "Responds {\"status\": \"ok\"} and echoes nothing — read back with:",
            "grimoire-cli models get --id <id>");
        command.AddExamples(
            "grimoire-cli models update --id <id> --input meta.json",
            "echo '{\"is_supported\":true}' | grimoire-cli models update --id <id> --stdin");
        command.AddRequestShape<Generated.Models.Model3DUpdate>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.Model3DUpdate.CreateFromDiscriminatorValue,
                    "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            var service = new ModelsService(client);
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
        var command = new Command("batch-update", "Update many models in one transaction")
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
            "and nothing is written. No tag may contain / or \\.",
            "",
            "is_supported cannot return to unknown here either — see models update.");
        command.AddExamples(
            "grimoire-cli models batch-update --input items.json",
            "jq -c '{items: .}' edits.json | grimoire-cli models batch-update --stdin");
        command.AddRequestShape<Generated.Models.Model3DBulkUpdate>();
        command.AddResponseExample<Generated.Models.BulkResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.Model3DBulkUpdate.CreateFromDiscriminatorValue,
                    "pass each id inside items");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            var service = new ModelsService(client);
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
        var command = new Command("batch-tag", "Add tags to many models, additively")
        {
            inputOption, stdinOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "1 to 1000 ids, and at least one tag; an empty list either side is a 422.",
            "",
            "Additive — it never removes a tag. models update replaces the set.",
            "",
            "Only an unresolved id lands in errors. Exit 3 is HTTP 200 with a",
            "non-empty errors list — a partial write. A tag containing / or \\ is a",
            "422 on the whole request, which writes nothing.");
        command.AddExamples(
            "grimoire-cli models batch-tag --input tags.json",
            "echo '{\"ids\":[\"<id>\"],\"tags\":[\"goblin\"]}' | grimoire-cli models batch-tag --stdin");
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
            var service = new ModelsService(client);
            var result = await service.BatchTagAsync(body);
            ConsoleOutput.WriteRawJson(result);
            return BulkExit.CodeFor(GrimoireApiClient.HasItems(result, "errors"));
        });
        return command;
    }
}
