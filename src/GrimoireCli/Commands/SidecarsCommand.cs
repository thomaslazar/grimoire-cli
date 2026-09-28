using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class SidecarsCommand
{
    private static readonly string[] SidecarFormats = ["opf", "nfo", "json", "yaml"];

    public static Command Create()
    {
        var command = new Command("sidecars", "Read and write metadata sidecar files beside each book");
        command.Subcommands.Add(CreateSettingsCommand());
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
            Description = "Allow a backfill to replace sidecars Grimoire did not write",
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
            "--overwrite-foreign lets a backfill replace hand-maintained .opf files.");
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
}
