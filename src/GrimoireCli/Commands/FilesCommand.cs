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
            "Merged with the index: record_id and title mark an indexed row, and their",
            "absence marks a loose file the scanner has not taken.",
            "",
            "Capped at 2000 entries — read total and truncated before treating the",
            "listing as complete. child_count per folder stops counting at 1000.",
            "",
            "singletons_taken reports which one-of-a-kind container kinds already",
            "exist, and writable whether the library mount allows writes.",
            "",
            "Dotfiles are excluded, as are sidecars (.opf, .nfo, .grimoire.yaml, an",
            "exported cover) sitting beside content with the same stem — and total is",
            "counted after that filter, so neither reports them.");
        command.AddExamples(
            "grimoire-cli files browse",
            "grimoire-cli files browse --path books --limit 100");
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
            "Sends one file per request, as the server requires — loop for many, so a",
            "failure names the file it happened on.",
            "",
            "Defaults to renaming on a collision and never overwrites. The server",
            "refuses above 8 GiB with 413; this CLI reads the file into memory, so in",
            "practice keep it under about 2 GiB.",
            "",
            "The file lands under a temporary name and is renamed into place once it is",
            "fully written, so an interrupted upload leaves nothing for the scanner.");
        command.AddExamples(
            "grimoire-cli files upload --destination \"books/D&D 5e\" --file ./phb.pdf",
            "for f in *.pdf; do grimoire-cli files upload --destination books --file \"$f\"; done");
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
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Defaults to skipping a collision and reporting it, where upload renames.",
            "Never overwrites either way.",
            "",
            "One request for every source: moved and skipped report per path.");
        command.AddExamples(
            "grimoire-cli files move --sources books/loose.pdf --destination \"books/D&D 5e\"",
            "grimoire-cli files move --sources a.pdf b.pdf --destination books --on-conflict rename");
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
            "The records count is how many indexed rows followed the rename.",
            "Sidecars beside the file are renamed with it.");
        command.AddExamples("grimoire-cli files rename --path books/old.pdf --new-name new.pdf");
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
        var deleteFilesOption = new Option<bool>("--delete-files") { Description = "Also unlink the files; irreversible" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("delete", "Remove a file or folder from the index, or from disk")
        {
            pathOption, confirmNameOption, deleteFilesOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Soft by default: the indexed rows go, the files stay, and a rescan re-adds",
            "whatever is still on disk. Works on a read-only library.",
            "",
            "--delete-files is irreversible — the file is unlinked rather than moved to",
            "a trash folder, and the row goes with its tags, favorites, bookmarks,",
            "progress and campaign links. files folder delete is always this form.",
            "",
            "428 when the target is a folder still holding content and --confirm-name",
            "is absent or does not match its name.");
        command.AddExamples(
            "grimoire-cli files delete --path books/gone.pdf",
            "grimoire-cli files delete --path books/old --confirm-name old --delete-files");
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
