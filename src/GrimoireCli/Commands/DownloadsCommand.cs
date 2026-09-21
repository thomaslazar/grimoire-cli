using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class DownloadsCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("downloads", "Bulk downloads");
        command.Subcommands.Add(CreateArchiveCommand());
        return command;
    }

    private static Command CreateArchiveCommand()
    {
        var typeOption = new Option<string>("--type") { Description = "Which slice of the library to archive", Required = true };
        var fmtOption = new Option<string?>("--fmt") { Description = "Archive format; the server uses zip when omitted" };
        var idOption = new Option<string?>("--id") { Description = "System ID" };
        var categoryOption = new Option<string?>("--category") { Description = "Book category slug" };
        var tagOption = new Option<string?>("--tag") { Description = "Tag internal key, from tags list" };
        var resourceTypeOption = new Option<string?>("--resource-type") { Description = "Restrict a tag scope to this resource type (book | map | token | audio | model)" };
        var folderOption = new Option<string?>("--folder") { Description = "Folder path" };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var command = new Command("archive", "Download a slice of the library as one archive")
        {
            typeOption, fmtOption, idOption, categoryOption, tagOption,
            resourceTypeOption, folderOption, outputOption
        };
        command.AddHelpSection("Scopes", HelpSectionPosition.Top,
            "--type selects the slice, and decides which other flags it needs:",
            "",
            "  system            --id",
            "  system_category   --id --category",
            "  book_folder       --id --folder",
            "  map_folder        --folder",
            "  token_folder      --folder",
            "  audio_folder      --folder",
            "  model_folder      --folder",
            "  library_folder    --folder   (admin; any folder on disk, indexed",
            "                                or not, unfiltered by book access)",
            "  tag               --tag",
            "  tag_type          --tag --resource-type",
            "  tag_folder        --tag --resource-type --folder",
            "",
            "For tag_folder, --folder is the group's path exactly as tags",
            "items returned it, not a filesystem path.",
            "",
            "A missing one is 400 naming the flag and the type.");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--fmt takes zip, tar, tar.gz or tar.bz2.",
            "",
            "A scope that resolves to no files is 404, including a container",
            "system that holds no books of its own.",
            "",
            "--output - writes the archive to stdout; a path writes it and",
            "prints {path, bytes}.",
            "",
            "The server builds the whole archive before sending anything but",
            "headers, so a very large scope can exceed the client's request timeout.");
        command.AddExamples(
            "grimoire-cli downloads archive --type system --id <system-id> --output system.zip",
            "grimoire-cli downloads archive --type tag --tag session-prep --output prep.zip",
            "grimoire-cli downloads archive --type tag_type --tag session-prep --resource-type map --fmt tar.gz --output maps.tar.gz");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new DownloadsService(client);
            await using var stream = await service.ArchiveAsync(
                parseResult.GetValue(typeOption)!,
                parseResult.GetValue(fmtOption),
                parseResult.GetValue(idOption),
                parseResult.GetValue(categoryOption),
                parseResult.GetValue(tagOption),
                parseResult.GetValue(resourceTypeOption),
                parseResult.GetValue(folderOption));
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
}
