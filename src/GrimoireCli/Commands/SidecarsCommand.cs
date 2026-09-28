using System.CommandLine;
using GrimoireCli.Api;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class SidecarsCommand
{
    private static readonly string[] SidecarFormats = ["opf", "nfo", "json", "yaml"];

    public static Command Create()
    {
        var command = new Command("sidecars", "Configure and backfill metadata sidecar files beside each book");
        command.Subcommands.Add(CreateSettingsCommand());
        command.Subcommands.Add(CreateExportCommand());
        return command;
    }

    private static Command CreateSettingsCommand()
    {
        var command = new Command("settings", "Sidecar export configuration");
        command.Subcommands.Add(CreateSettingsGetCommand());
        command.Subcommands.Add(CreateSettingsSetCommand());
        return command;
    }

    private static Command CreateSettingsGetCommand()
    {
        var command = new Command("get", "Read the sidecar export configuration");
        command.AddRoleRequired("admin");
        command.AddExamples("grimoire-cli sidecars settings get");
        command.AddResponseExample<Generated.Models.SidecarSettings>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SidecarsService(client);
            var result = await service.SettingsAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateSettingsSetCommand()
    {
        var formatsOption = new Option<string[]>("--formats")
        {
            Description = "Sidecar formats to enable; repeatable",
            AllowMultipleArgumentsPerToken = true,
            Required = true,
        };
        formatsOption.Validators.Add(result =>
        {
            foreach (var value in result.GetValueOrDefault<string[]>() ?? [])
            {
                if (!SidecarFormats.Contains(value))
                    result.AddError(
                        $"'{value}' is not a valid value for --formats. Must be one of: {string.Join(", ", SidecarFormats)}");
            }
        });
        formatsOption.CompletionSources.Add(SidecarFormats);
        var coversOption = new Option<bool>("--covers") { Description = "Write the cover image beside the metadata file" };
        var overwriteForeignOption = new Option<bool>("--overwrite-foreign")
        {
            Description = "With --covers, replace a foreign cover; sidecar files are never rewritten",
        };
        var command = new Command("set", "Configure which sidecar formats are written")
        {
            formatsOption, coversOption, overwriteForeignOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Replaces the whole settings object: an omitted --covers or",
            "--overwrite-foreign is set false.",
            "",
            "--overwrite-foreign only affects export's --covers: a foreign cover can",
            "be replaced. Metadata sidecar files are never rewritten by export,",
            "regardless of this setting.",
            "",
            "--formats is required, so sidecar export cannot be disabled from the CLI.");
        command.AddExamples(
            "grimoire-cli sidecars settings set --formats opf",
            "grimoire-cli sidecars settings set --formats opf json --covers");
        command.AddResponseExample<Generated.Models.SidecarSettings>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SidecarsService(client);
            var result = await service.SettingsSetAsync(
                parseResult.GetValue(formatsOption)!,
                parseResult.GetValue(coversOption),
                parseResult.GetValue(overwriteForeignOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateExportCommand()
    {
        var command = new Command("export", "Backfill metadata sidecars for books that have none");
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Writes only the sidecars that are missing and never rewrites one that",
            "exists. No need to re-run after a metadata sweep: an edit",
            "refreshes a book's existing sidecars on its own, and the scanner",
            "writes them for new books.",
            "",
            "Books only — maps, tokens, audio and models get nothing.",
            "",
            "400 until a format is enabled: grimoire-cli sidecars settings set.",
            "409 while a library scan is running. Runs inline, so there is no",
            "status to poll.",
            "",
            "read_only true means the library mount is not writable.",
            "",
            "Exit 3 when failed is above 0; a foreign skip alone leaves it at 0.",
            "stdout still carries the full JSON either way.");
        command.AddExamples("grimoire-cli sidecars export");
        command.AddResponseExample<Generated.Models.SidecarExportResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SidecarsService(client);
            var result = await service.ExportAsync();
            ConsoleOutput.WriteRawJson(result);
            return BulkExit.CodeFor(GrimoireApiClient.IsPositive(result, "failed"));
        });
        return command;
    }
}
