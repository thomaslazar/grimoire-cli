using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CoverCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("cover", "The system's cover image");
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateUploadCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateFromSourceCommand());
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var command = new Command("get", "Download the system's cover image")
        {
            idOption, outputOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Serves folder cover art if the system's library folder has a cover.* or",
            "folder.* image, otherwise the uploaded cover. 404 when it has neither —",
            "fall back to cover_book_id from systems get and books thumbnail.",
            "",
            "--output - writes the image to stdout; a path writes the file and prints",
            "{path, bytes}.");
        command.AddExamples(
            "grimoire-cli systems cover get --id <system-id> --output cover.png",
            "grimoire-cli systems cover get --id <system-id> --output - > cover.png");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemsService(client);
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
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var fileOption = new Option<string>("--file") { Description = "Path to a PNG, JPEG, WebP or GIF", Required = true };
        var command = new Command("upload", "Upload the system's cover image")
        {
            idOption, fileOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "PNG, JPEG, WebP or GIF, max 10 MB; the content type is taken from the",
            "file extension. Replaces any existing upload.",
            "",
            "Folder cover art still wins, so cover get may keep returning the library",
            "image. 400 if the bytes are not a decodable image of the declared type.");
        command.AddExamples("grimoire-cli systems cover upload --id <system-id> --file cover.png");
        command.AddResponseExample<Generated.Models.SystemCoverResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemsService(client);
            string result;
            try
            {
                result = await service.UploadCoverAsync(parseResult.GetValue(idOption)!, parseResult.GetValue(fileOption)!);
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

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var command = new Command("delete", "Delete the system's uploaded cover image")
        {
            idOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Removes the uploaded cover only; folder cover art is library-managed and",
            "survives. Exits 0 whether or not one was uploaded.",
            "",
            "Responds {\"status\": \"ok\"}.");
        command.AddExamples("grimoire-cli systems cover delete --id <system-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemsService(client);
            var response = await service.DeleteCoverAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(response);
            return 0;
        });
        return command;
    }

    private static Command CreateFromSourceCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var sourceTypeOption = new Option<string>("--source-type") { Description = "The kind of library item to copy the image from", Required = true };
        var sourceIdOption = new Option<string>("--source-id") { Description = "That item's ID", Required = true };
        var command = new Command("from-source", "Set the system's cover from an image already in the library")
        {
            idOption, sourceTypeOption, sourceIdOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--source-type takes map, token, book or audio. The API also declares",
            "campaign_file, which needs a campaign this route never sends, so it",
            "always 400s here.",
            "",
            "Copies the bytes in as an upload does, so a folder cover.* or folder.*",
            "image still wins over what this sets.",
            "",
            "Useful for a container system, which has no books of its own to take a",
            "thumbnail from.");
        command.AddExamples(
            "grimoire-cli systems cover from-source --id <system-id> --source-type book --source-id <book-id>");
        command.AddResponseExample<Generated.Models.SystemCoverResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemsService(client);
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
