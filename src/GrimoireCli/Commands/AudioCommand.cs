using System.CommandLine;
using GrimoireCli.Api;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class AudioCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("audio", "Read and edit audio metadata");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateArtworkCommand());
        command.Subcommands.Add(CreateFileCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateBatchUpdateCommand());
        command.Subcommands.Add(CreateBatchTagCommand());
        command.Subcommands.Add(AudioFolderCommands.Create());
        return command;
    }

    private static Command CreateListCommand()
    {
        var limitOption = OptionHelpers.Range("--limit", "Results per page (the server sets no maximum)", 1);
        limitOption.DefaultValueFactory = _ => 100;
        var offsetOption = OptionHelpers.Range("--offset", "Items to skip", 0);
        var command = new Command("list", "List audio tracks")
        {
            limitOption, offsetOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Variants are hidden — only the main copy of a family is listed.",
            "",
            "Page with --offset against total in the response.");
        command.AddExamples(
            "grimoire-cli audio list",
            "grimoire-cli audio list --limit 20 --offset 100");
        command.AddResponseExample<Generated.Models.AudioListResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new AudioService(client);
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
        var idOption = new Option<string>("--id") { Description = "Audio track ID", Required = true };
        var command = new Command("get", "Get one audio track")
        {
            idOption
        };
        command.AddExamples("grimoire-cli audio get --id <audio-id>");
        command.AddResponseExample<Generated.Models.AudioDetailResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new AudioService(client);
            var result = await service.GetAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateArtworkCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Audio track ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var command = new Command("artwork", "Download the audio track's artwork")
        {
            idOption, outputOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Resolves three sources in order: a cover set deliberately, then folder art,",
            "then art embedded in the file. 404 when the track has none —",
            "has_artwork in audio list says whether any of the three exists.",
            "",
            "--output - writes the image to stdout; a path writes the file and",
            "prints {path, bytes}.");
        command.AddExamples(
            "grimoire-cli audio artwork --id <id> --output cover.jpg",
            "grimoire-cli audio artwork --id <id> --output - > cover.jpg");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new AudioService(client);
            await using var stream = await service.ArtworkAsync(parseResult.GetValue(idOption)!);
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

    private static Command CreateFileCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Audio ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var command = new Command("file", "Download the audio file")
        {
            idOption, outputOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The original track as stored — audio artwork serves the embedded",
            "cover image instead.",
            "",
            "--output - writes the file to stdout; a path writes it and prints",
            "{path, bytes}.");
        command.AddExamples(
            "grimoire-cli audio file --id <id> --output track.mp3",
            "grimoire-cli audio file --id <id> --output - > track.mp3");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new AudioService(client);
            await using var stream = await service.FileAsync(parseResult.GetValue(idOption)!);
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
        var idOption = new Option<string>("--id") { Description = "Audio track ID", Required = true };
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("update", "Update one audio track's metadata")
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
            "duration, title, artist and album are read from the file at scan",
            "time and cannot be set here.",
            "",
            "Responds {\"status\": \"ok\"} and echoes nothing — read back with:",
            "grimoire-cli audio get --id <id>");
        command.AddExamples(
            "grimoire-cli audio update --id <id> --input meta.json",
            "echo '{\"description\":\"tavern loop\"}' | grimoire-cli audio update --id <id> --stdin");
        command.AddRequestShape<Generated.Models.AudioUpdate>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.AudioUpdate.CreateFromDiscriminatorValue,
                    "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            var service = new AudioService(client);
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
        var command = new Command("batch-update", "Update many audio tracks in one transaction")
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
            "and nothing is written. No tag may contain / or \\.");
        command.AddExamples(
            "grimoire-cli audio batch-update --input items.json",
            "jq -c '{items: .}' edits.json | grimoire-cli audio batch-update --stdin");
        command.AddRequestShape<Generated.Models.AudioBulkUpdate>();
        command.AddResponseExample<Generated.Models.BulkResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.AudioBulkUpdate.CreateFromDiscriminatorValue,
                    "pass each id inside items");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            var service = new AudioService(client);
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
        var command = new Command("batch-tag", "Add tags to many audio tracks, additively")
        {
            inputOption, stdinOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "1 to 1000 ids, and at least one tag; an empty list either side is a 422.",
            "",
            "Additive — it never removes a tag. audio update replaces the set.",
            "",
            "Only an unresolved id lands in errors. Exit 3 is HTTP 200 with a",
            "non-empty errors list — a partial write. A tag containing / or \\ is a",
            "422 on the whole request, which writes nothing.");
        command.AddExamples(
            "grimoire-cli audio batch-tag --input tags.json",
            "echo '{\"ids\":[\"<id>\"],\"tags\":[\"ambience\"]}' | grimoire-cli audio batch-tag --stdin");
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
            var service = new AudioService(client);
            var result = await service.BatchTagAsync(body);
            ConsoleOutput.WriteRawJson(result);
            return BulkExit.CodeFor(GrimoireApiClient.HasItems(result, "errors"));
        });
        return command;
    }
}
