using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class BookFolderCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("book-folders", "Subcategory folders and their tags");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateSetCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("list", "List a system's tagged subcategory folders")
        {
            idOption, serverOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Folders that have been tagged, not the folders on disk — a record is",
            "created only by book-folders set. Nothing enumerates the tree.",
            "",
            "A folder's tags apply to every book at or below its path and never",
            "appear in a book's own tags, so books get will not show them.",
            "",
            "Tags read back in display casing; set echoes internal keys.");
        command.AddExamples("grimoire-cli systems book-folders list --id <system-id>");
        command.AddResponseExample<Generated.Models.BookFoldersResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new SystemsService(client);
            var result = await service.BookFoldersAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateSetCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("set", "Set a subcategory folder's tags")
        {
            idOption, inputOption, stdinOption, serverOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Replaces the folder's tag list; batch-tag adds. An empty tags array",
            "clears the folder but keeps it — book-folders delete removes it.",
            "",
            "path is {system-id}/{category}/{subfolder}, where subfolder is the",
            "segments of a book's relative_path between the category directory and",
            "the filename. Its first segment must be the same system as --id.",
            "",
            "Creates the folder record if the path has none.");
        command.AddExamples(
            "grimoire-cli systems book-folders set --id <system-id> --input folder.json",
            "echo '{\"path\":\"<id>/core/errata\",\"tags\":[\"errata\"]}' | grimoire-cli systems book-folders set --id <system-id> --stdin");
        command.AddRequestShape<Generated.Models.BookFolderUpdate>();
        command.AddResponseExample<Generated.Models.BookFolderOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.BookFolderUpdate.CreateFromDiscriminatorValue,
                    "the folder is addressed by path");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new SystemsService(client);
            var result = await service.SetBookFolderAsync(parseResult.GetValue(idOption)!, body);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var pathOption = new Option<string>("--path") { Description = "Folder path to remove", Required = true };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("delete", "Remove a subcategory folder's record")
        {
            idOption, pathOption, serverOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Removes the record, so the books below the path stop inheriting its",
            "tags. Clearing the tags with book-folders set leaves the record.",
            "",
            "A path with no record is a 404, so this is not repeatable.");
        command.AddExamples(
            "grimoire-cli systems book-folders delete --id <system-id> --path <system-id>/core/errata");
        command.AddResponseExample<Generated.Models.Backend__routers__systems___schemas__StatusResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new SystemsService(client);
            var result = await service.DeleteBookFolderAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(pathOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
