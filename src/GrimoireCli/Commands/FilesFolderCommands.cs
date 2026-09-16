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
        var command = new Command("folder", "Folders in the library tree; delete them with files delete");
        command.Subcommands.Add(CreateCreateCommand());
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
        var framesOption = new Option<bool>("--frames-container") { Description = "Mark it as holding token-editor frame art" };
        var command = new Command("create", "Create a folder, optionally as a container, a frame folder, or NSFW")
        {
            parentOption, nameOption, containerKindOption, nsfwOption, framesOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--container-kind only applies where a game system belongs: inside books/,",
            "at a depth reached through containers alone. one-page and agnostic may",
            "exist only once in the library.",
            "",
            "--frames-container only applies under tokens/, at any depth below it.",
            "",
            "Either marker outside its own tree is 400. files browse reports",
            "children_accept_container_kind, children_accept_frames_marker and",
            "singletons_taken for the --parent folder.");
        command.AddExamples(
            "grimoire-cli files folder create --parent books --name \"Call of Cthulhu\"",
            "grimoire-cli files folder create --parent books --name Publishers --container-kind publisher");
        command.AddResponseExample<Generated.Models.FolderResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new FilesService(client);
            var result = await service.CreateFolderAsync(
                parseResult.GetValue(parentOption)!,
                parseResult.GetValue(nameOption)!,
                parseResult.GetValue(containerKindOption),
                parseResult.GetValue(nsfwOption),
                parseResult.GetValue(framesOption));
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
        var framesOption = new Option<bool?>("--frames-container") { Description = "Frame-folder flag (true | false)" };
        var command = new Command("markers", "Set a folder's container/NSFW/frame markers")
        {
            pathOption, containerKindOption, nsfwOption, framesOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Omitted fields are left alone.",
            "",
            "Setting --container-kind needs a folder where a game system belongs",
            "(inside books/, at a depth reached through containers alone); setting",
            "--frames-container needs one under tokens/. Either outside its own tree is",
            "400. Clearing is always allowed, so a marker written by hand in the wrong",
            "place stays removable. files browse reports accepts_container_kind and",
            "accepts_frames_marker per row.");
        command.AddExamples(
            "grimoire-cli files folder markers --path \"books/Kult\" --nsfw true",
            "grimoire-cli files folder markers --path \"books/Publishers\" --container-kind publisher");
        command.AddResponseExample<Generated.Models.FolderResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new FilesService(client);
            var result = await service.MarkersAsync(
                parseResult.GetValue(pathOption)!,
                parseResult.GetValue(containerKindOption),
                parseResult.GetValue(nsfwOption),
                parseResult.GetValue(framesOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateScaffoldCommand()
    {
        var pathOption = new Option<string>("--path") { Description = "System folder to scaffold", Required = true };
        var command = new Command("scaffold", "Create the standard category folders in a system folder")
        {
            pathOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Creates Core, Supplements, Adventures, Character Sheets, Maps, Handouts,",
            "Homebrew and Starter Sets. Re-running is safe.",
            "",
            "--path must be a system folder: books/ itself and container folders",
            "hold systems, not categories, and are refused with 400. files browse",
            "reports category_host per row.");
        command.AddExamples("grimoire-cli files folder scaffold --path \"books/Dungeons & Dragons/5e EN\"");
        command.AddResponseExample<Generated.Models.ScaffoldResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
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
        var command = new Command("contents", "Report whether a folder holds content")
        {
            pathOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "has_content false means folder delete needs no --confirm-name.");
        command.AddExamples("grimoire-cli files folder contents --path \"books/Old Imports\"");
        command.AddResponseExample<Generated.Models.FolderContentsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new FilesService(client);
            var result = await service.FolderContentsAsync(parseResult.GetValue(pathOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
