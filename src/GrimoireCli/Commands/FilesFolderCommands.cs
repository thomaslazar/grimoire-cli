using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

/// <summary>
/// Folder management under `files`. POST and DELETE share /api/files/folder, so
/// the group nests; markers, scaffold and contents are sibling paths and stay
/// flat leaves under it. Distinct from BookFolderCommands, which serves
/// `systems book-folders` — a tagging layer, not the tree on disk.
/// </summary>
public static class FilesFolderCommands
{
    private static readonly string[] ContainerKinds =
        ["parent", "one-page", "agnostic", "family", "publisher", "generic"];

    // markers can clear a marker, which the server expresses as an empty
    // container_kind (folders.py removes every marker and writes none). create
    // cannot: a new folder has nothing to clear.
    private static readonly string[] MarkerContainerKinds = [.. ContainerKinds, ""];

    public static Command Create()
    {
        var command = new Command("folder", "Folders in the library tree");
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateMarkersCommand());
        command.Subcommands.Add(CreateScaffoldCommand());
        command.Subcommands.Add(CreateContentsCommand());
        return command;
    }

    private static Command CreateCreateCommand()
    {
        var parentOption = new Option<string>("--parent") { Description = "Folder to create it in", Required = true };
        var nameOption = new Option<string>("--name") { Description = "New folder's name", Required = true };
        var containerKindOption = OptionHelpers.Choice("--container-kind", "Mark it as a container of this kind", ContainerKinds);
        var nsfwOption = new Option<bool>("--nsfw") { Description = "Mark it NSFW" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("create", "Create a folder, optionally as a container or NSFW")
        {
            parentOption, nameOption, containerKindOption, nsfwOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "one-page and agnostic may exist only once in the library, and are",
            "recognised only at the top level of books/ — files browse reports",
            "singletons_taken for the ones already gone.");
        command.AddExamples(
            "grimoire-cli files folder create --parent books --name \"Call of Cthulhu\"",
            "grimoire-cli files folder create --parent books --name Publishers --container-kind publisher");
        command.AddResponseExample<Generated.Models.FolderResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            var result = await service.CreateFolderAsync(
                parseResult.GetValue(parentOption)!,
                parseResult.GetValue(nameOption)!,
                parseResult.GetValue(containerKindOption),
                parseResult.GetValue(nsfwOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var pathOption = new Option<string>("--path") { Description = "Folder to delete", Required = true };
        var confirmNameOption = new Option<string?>("--confirm-name") { Description = "The folder's own name, required when it holds content" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("delete", "Delete a folder, recursively when confirmed by name")
        {
            pathOption, confirmNameOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Always deletes the files, and cannot be undone. files delete leaves them",
            "unless --delete-files is passed; this has no such option.",
            "",
            "An empty folder, or one holding only markers and empty descendants, goes",
            "without confirmation. One still holding content is 428 until",
            "--confirm-name matches its own name.");
        command.AddExamples("grimoire-cli files folder delete --path \"books/Old Imports\" --confirm-name \"Old Imports\"");
        command.AddResponseExample<Generated.Models.DeleteResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            var result = await service.DeleteFolderAsync(
                parseResult.GetValue(pathOption)!,
                parseResult.GetValue(confirmNameOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateMarkersCommand()
    {
        var pathOption = new Option<string>("--path") { Description = "Folder to mark", Required = true };
        var containerKindOption = OptionHelpers.Choice("--container-kind", "Container kind; pass \"\" to clear it", MarkerContainerKinds);
        var nsfwOption = new Option<bool?>("--nsfw") { Description = "NSFW flag (true | false)" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("markers", "Set a folder's container/NSFW markers")
        {
            pathOption, containerKindOption, nsfwOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Omitted fields are left alone.");
        command.AddExamples(
            "grimoire-cli files folder markers --path \"books/Kult\" --nsfw true",
            "grimoire-cli files folder markers --path \"books/Publishers\" --container-kind publisher");
        command.AddResponseExample<Generated.Models.FolderResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            var result = await service.MarkersAsync(
                parseResult.GetValue(pathOption)!,
                parseResult.GetValue(containerKindOption),
                parseResult.GetValue(nsfwOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateScaffoldCommand()
    {
        var pathOption = new Option<string>("--path") { Description = "System folder to scaffold", Required = true };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("scaffold", "Create the standard category folders in a system folder")
        {
            pathOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Creates Core, Supplements, Adventures, Character Sheets, Maps, Handouts,",
            "Homebrew and Starter Sets. Re-running is safe.");
        command.AddExamples("grimoire-cli files folder scaffold --path \"books/Dungeons & Dragons/5e EN\"");
        command.AddResponseExample<Generated.Models.ScaffoldResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            var result = await service.ScaffoldAsync(parseResult.GetValue(pathOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateContentsCommand()
    {
        var pathOption = new Option<string>("--path") { Description = "Folder to check", Required = true };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("contents", "Report whether a folder holds content")
        {
            pathOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "has_content false means folder delete needs no --confirm-name.");
        command.AddExamples("grimoire-cli files folder contents --path \"books/Old Imports\"");
        command.AddResponseExample<Generated.Models.FolderContentsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            var result = await service.FolderContentsAsync(parseResult.GetValue(pathOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
