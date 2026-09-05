using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class FilesCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    internal static readonly string[] ConflictPolicies = ["skip", "rename"];

    public static Command Create()
    {
        var command = new Command("files", "The library tree on disk");
        command.Subcommands.Add(CreateBrowseCommand());
        command.Subcommands.Add(CreateUploadCommand());
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
            "exist, and writable whether the library mount allows writes.");
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
            "Defaults to renaming on a collision and never overwrites. 413 above 8 GiB.",
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
}
