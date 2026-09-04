using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class BackupsCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("backups", "Server backups of the database and user assets");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateDownloadCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("list", "List backups, newest first")
        {
            serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Reports directory and total_bytes alongside the rows.",
            "",
            "version is the app version that wrote the archive, or unknown when its",
            "manifest is unreadable.");
        command.AddExamples("grimoire-cli backups list");
        command.AddResponseExample<Generated.Models.BackupListResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new BackupsService(client);
            var result = await service.ListAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateCreateCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("create", "Take a backup now")
        {
            serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Snapshots the database under a read lock, so writes are held off until it",
            "finishes — brief for a typical library, not instant.",
            "",
            "409 if a backup is already running. Writes to the data directory, not the",
            "library, so a read-only library mount does not block it.");
        command.AddExamples("grimoire-cli backups create");
        command.AddResponseExample<Generated.Models.BackupItem>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new BackupsService(client);
            var result = await service.CreateAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Backup ID", Required = true };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("delete", "Delete one backup archive")
        {
            idOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Deletes the archive from disk. This cannot be undone, and there is no",
            "confirmation prompt.",
            "",
            "Answers 204: stdout carries no body.");
        command.AddExamples("grimoire-cli backups delete --id <backup-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new BackupsService(client);
            var result = await service.DeleteAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDownloadCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Backup ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for the zip on stdout",
            Required = true,
        };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("download", "Download one backup archive")
        {
            idOption, outputOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Serves the archive as application/zip.",
            "",
            "There is no restore endpoint: the archive is the whole recovery path, and",
            "putting one back is out of band.",
            "",
            "--output - writes the zip to stdout; a path writes the file and prints",
            "{path, bytes}.");
        command.AddExamples(
            "grimoire-cli backups download --id <backup-id> --output backup.zip",
            "grimoire-cli backups download --id <backup-id> --output - > backup.zip");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new BackupsService(client);
            await using var stream = await service.DownloadAsync(parseResult.GetValue(idOption)!);
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
