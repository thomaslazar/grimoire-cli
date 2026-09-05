using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class FilesCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly string[] ConflictPolicies = ["skip", "rename"];

    public static Command Create()
    {
        var command = new Command("files", "The library tree on disk");
        command.Subcommands.Add(CreateBrowseCommand());
        command.Subcommands.Add(CreateUploadCommand());
        command.Subcommands.Add(CreateMoveCommand());
        command.Subcommands.Add(CreateRenameCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(FilesFolderCommands.Create());
        return command;
    }

    private static Command CreateBrowseCommand()
    {
        var pathOption = new Option<string?>("--path") { Description = "Folder to list; omit for the library root" };
        var limitOption = OptionHelpers.Range("--limit", "Entries to return; default and cap 2000", 1, 2000);
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("browse", "List a library folder with indexing state")
        {
            pathOption, limitOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Lists what is on disk, and which of it Grimoire has indexed: an entry",
            "with record_id and title is in the catalogue; one without is present on",
            "disk but not indexed.",
            "",
            "Capped at 2000 entries — read total and truncated before treating the",
            "listing as complete. child_count per folder stops counting at 1000.",
            "",
            "Dotfiles are excluded, as are sidecars (.opf, .nfo, .grimoire.yaml, an",
            "exported cover) sitting beside content with the same stem — and total is",
            "counted after that filter, so neither reports them.");
        command.AddExamples(
            "grimoire-cli files browse",
            "grimoire-cli files browse --path \"books/Dungeons & Dragons/5e EN\" --limit 100");
        command.AddResponseExample<Generated.Models.BrowseResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            var result = await service.BrowseAsync(
                parseResult.GetValue(pathOption),
                parseResult.GetValue(limitOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateUploadCommand()
    {
        var destinationOption = new Option<string>("--destination") { Description = "Library folder to upload into", Required = true };
        var fileOption = new Option<string>("--file") { Description = "Local file to upload", Required = true };
        var relativeDirOption = new Option<string?>("--relative-dir") { Description = "Sub-path under the destination, created if missing" };
        var onConflictOption = OptionHelpers.Choice("--on-conflict", "Collision policy; default rename", ConflictPolicies);
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("upload", "Upload a single file into a library folder")
        {
            destinationOption, fileOption, relativeDirOption, onConflictOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The server refuses above 8 GiB with 413; this CLI reads the file into",
            "memory, so in practice keep it under about 2 GiB.");
        command.AddExamples(
            "grimoire-cli files upload --destination \"books/Call of Cthulhu/7e EN/core\" --file \"Keeper Rulebook.pdf\"");
        command.AddResponseExample<Generated.Models.UploadResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            try
            {
                var result = await service.UploadAsync(
                    parseResult.GetValue(destinationOption)!,
                    parseResult.GetValue(fileOption)!,
                    parseResult.GetValue(relativeDirOption),
                    parseResult.GetValue(onConflictOption));
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

    private static Command CreateMoveCommand()
    {
        var sourcesOption = new Option<string[]>("--sources")
        {
            Description = "Paths to move; repeatable",
            Required = true,
            AllowMultipleArgumentsPerToken = true,
        };
        var destinationOption = new Option<string>("--destination") { Description = "Destination folder", Required = true };
        var onConflictOption = OptionHelpers.Choice("--on-conflict", "Collision policy; default skip", ConflictPolicies);
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("move", "Move files or folders, preserving their metadata")
        {
            sourcesOption, destinationOption, onConflictOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddExamples(
            "grimoire-cli files move --sources \"books/Keeper Rulebook.pdf\" --destination \"books/Call of Cthulhu/7e EN/core\"",
            "grimoire-cli files move --sources \"Berlin.pdf\" \"Cyberpirates.pdf\" --destination \"books/Shadowrun/5 DE\" --on-conflict rename");
        command.AddResponseExample<Generated.Models.MoveResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            var result = await service.MoveAsync(
                parseResult.GetValue(sourcesOption)!,
                parseResult.GetValue(destinationOption)!,
                parseResult.GetValue(onConflictOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateRenameCommand()
    {
        var pathOption = new Option<string>("--path") { Description = "Path to rename", Required = true };
        var newNameOption = new Option<string>("--new-name") { Description = "New name, without any path", Required = true };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("rename", "Rename a file or folder on disk")
        {
            pathOption, newNameOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The records count is how many index entries followed the rename.",
            "Sidecars beside the file are renamed with it.");
        command.AddExamples("grimoire-cli files rename --path \"books/phb.pdf\" --new-name \"Player's Handbook.pdf\"");
        command.AddResponseExample<Generated.Models.RenameResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            var result = await service.RenameAsync(
                parseResult.GetValue(pathOption)!,
                parseResult.GetValue(newNameOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var pathOption = new Option<string>("--path") { Description = "File or folder to remove", Required = true };
        var confirmNameOption = new Option<string?>("--confirm-name") { Description = "The folder's own name, required when it holds content" };
        var deleteFilesOption = new Option<bool>("--delete-files") { Description = "Also delete the files from disk; irreversible" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("delete", "Remove a file or folder from the index, or from disk")
        {
            pathOption, confirmNameOption, deleteFilesOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Soft by default: the index entries go, the files stay, and a rescan",
            "re-adds whatever is still on disk. Works on a read-only library.",
            "",
            "--delete-files also deletes the files and cannot be undone: nothing is",
            "moved to a trash folder, and the item's tags, favorites, bookmarks,",
            "progress and campaign links go with it. files folder delete always",
            "deletes the files.",
            "",
            "428 when the target is a folder still holding content and --confirm-name",
            "is absent or does not match its name.");
        command.AddExamples(
            "grimoire-cli files delete --path \"books/Monster Manual (copy).pdf\"",
            "grimoire-cli files delete --path \"books/Old Imports\" --confirm-name \"Old Imports\" --delete-files");
        command.AddResponseExample<Generated.Models.DeleteResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            var result = await service.DeleteAsync(
                parseResult.GetValue(pathOption)!,
                parseResult.GetValue(confirmNameOption),
                parseResult.GetValue(deleteFilesOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
