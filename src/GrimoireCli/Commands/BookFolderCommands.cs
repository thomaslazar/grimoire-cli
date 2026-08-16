using System.CommandLine;
using GrimoireCli.Models;
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
        return command;
    }

    private static Command CreateListCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("list", "List a system's subcategory folders and their tags")
        {
            idOption, serverOption, tokenOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Folders that have been tagged, not every subcategory folder on disk —",
            "a folder record is created only by book-folders set; scanning never",
            "creates one. A tagged folder's tags are inherited by every book at or",
            "below its path, but never appear in a book's own tags.",
            "",
            "Books sitting directly in a category directory belong to no folder.");
        command.AddExamples("grimoire-cli systems book-folders list --id <system-id>");
        command.AddResponseExample<BookFolderList>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new SystemsService(client);
            var result = await service.BookFoldersAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.BookFolderList);
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
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("set", "Set a subcategory folder's tags")
        {
            idOption, inputOption, stdinOption, serverOption, tokenOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Replaces the folder's tag list; batch-tag adds. An empty tags array",
            "clears it. Creates the folder record if the path has none.",
            "",
            "path is {system-id}/{category}/{subfolder}: subfolder is the segments",
            "of a book's relative_path between the category directory and the",
            "filename. The server ignores the --id in the URL and writes whatever",
            "path the body names, without checking that it belongs to this system",
            "or exists.",
            "",
            "Tags echo back as internal keys; book-folders list shows display casing.");
        command.AddExamples(
            "grimoire-cli systems book-folders set --id <system-id> --input folder.json",
            "echo '{\"path\":\"<id>/core/Curse of Strahd\",\"tags\":[\"horror\"]}' | grimoire-cli systems book-folders set --id <system-id> --stdin");
        command.AddRequestShape<Generated.Models.BookFolderUpdate>();
        command.AddResponseExample<BookFolderUpdated>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.BookFolderUpdate.CreateFromDiscriminatorValue,
                    "the folder is addressed by path; --id in the URL is ignored");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new SystemsService(client);
            var result = await service.SetBookFolderAsync(parseResult.GetValue(idOption)!, body);
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.BookFolderUpdated);
            return 0;
        });
        return command;
    }
}
