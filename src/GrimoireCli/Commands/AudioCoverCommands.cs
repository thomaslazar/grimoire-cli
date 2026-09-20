using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class AudioCoverCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("cover", "The track's deliberately-set cover image");
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateUploadCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateFromSourceCommand());
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Audio ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var command = new Command("get", "Download the track's set cover image")
        {
            idOption, outputOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Serves only a cover set through this group; 404 when the track has",
            "none, even if folder or embedded art exists. audio artwork resolves",
            "all three instead. has_cover in audio list says whether one is set.",
            "",
            "--output - writes the image to stdout; a path writes the file and",
            "prints {path, bytes}.");
        command.AddExamples("grimoire-cli audio cover get --id <audio-id> --output cover.png");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new AudioService(client);
            await using var stream = await service.CoverAsync(parseResult.GetValue(idOption)!);
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

    private static Command CreateUploadCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Audio ID", Required = true };
        var fileOption = new Option<string>("--file") { Description = "Path to a PNG, JPEG, WebP or GIF", Required = true };
        var command = new Command("upload", "Upload a cover image for the track")
        {
            idOption, fileOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Replaces any cover already set. The server checks the content type",
            "first, then the size, so an oversized image answers 413 and an",
            "unsupported one 400.");
        command.AddExamples("grimoire-cli audio cover upload --id <audio-id> --file cover.png");
        command.AddResponseExample<Generated.Models.AudioCoverResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new AudioService(client);
            try
            {
                var result = await service.UploadCoverAsync(
                    parseResult.GetValue(idOption)!,
                    parseResult.GetValue(fileOption)!);
                ConsoleOutput.WriteRawJson(result);
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

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Audio ID", Required = true };
        var command = new Command("delete", "Remove the track's set cover image")
        {
            idOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Folder and embedded art are untouched and take over, so has_artwork",
            "can stay true and audio artwork keep serving an image.",
            "",
            "Responds {\"status\": \"ok\"} whether or not a cover was set.");
        command.AddExamples("grimoire-cli audio cover delete --id <audio-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new AudioService(client);
            var result = await service.DeleteCoverAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateFromSourceCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Audio ID", Required = true };
        var sourceTypeOption = new Option<string>("--source-type") { Description = "The kind of library item to copy the image from", Required = true };
        var sourceIdOption = new Option<string>("--source-id") { Description = "That item's ID", Required = true };
        var command = new Command("from-source", "Set the track's cover from an image already in the library")
        {
            idOption, sourceTypeOption, sourceIdOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--source-type takes map, token, book or audio; campaign_file is not a",
            "valid value here (422) since a track has no campaign to resolve it",
            "against.",
            "",
            "Replaces any cover already set.");
        command.AddExamples(
            "grimoire-cli audio cover from-source --id <audio-id> --source-type book --source-id <book-id>");
        command.AddResponseExample<Generated.Models.AudioCoverResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new AudioService(client);
            var result = await service.CoverFromSourceAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(sourceTypeOption)!,
                parseResult.GetValue(sourceIdOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
