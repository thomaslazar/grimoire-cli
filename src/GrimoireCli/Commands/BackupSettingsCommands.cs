using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

/// <summary>
/// The backup schedule and retention pair. GET and PUT share one path, so they
/// nest as a subgroup the way `systems cover` does.
/// </summary>
public static class BackupSettingsCommands
{
    private static readonly string[] Schedules = ["off", "hourly", "daily", "weekly"];

    public static Command Create()
    {
        var command = new Command("settings", "Backup schedule and retention");
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateSetCommand());
        return command;
    }

    private static Command CreateGetCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("get", "Read the backup schedule and retention settings")
        {
            serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "A field whose *_env_locked is true is pinned by an environment variable;",
            "settings set answers 400 for it.");
        command.AddExamples("grimoire-cli backups settings get");
        command.AddResponseExample<Generated.Models.BackupSettingsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new BackupsService(client);
            var result = await service.SettingsAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateSetCommand()
    {
        var scheduleOption = OptionHelpers.Choice("--schedule", "How often to back up automatically", Schedules);
        var hourOption = OptionHelpers.Range("--hour", "Hour of day for the scheduled run", 0, 23);
        var minuteOption = OptionHelpers.Range("--minute", "Minute of the hour", 0, 59);
        var weekdayOption = OptionHelpers.Range("--weekday", "Day for a weekly schedule; 0=Mon", 0, 6);
        var retentionCountOption = OptionHelpers.Range("--retention-count", "Archives to keep; 0 for no limit", 0);
        var retentionGbOption = OptionHelpers.Range("--retention-gb", "Budget in GB; 0 for no limit", 0);
        var dirOption = new Option<string?>("--dir") { Description = "Backup directory; \"\" resets to the default" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("set", "Configure the backup schedule and retention")
        {
            scheduleOption, hourOption, minuteOption, weekdayOption,
            retentionCountOption, retentionGbOption, dirOption,
            serverOption
        };
        command.AddRoleRequired("admin");
        command.Validators.Add(result =>
        {
            // GetResult asks only whether the flag appeared, so an unconvertible
            // value reaches the framework's own parse error instead of throwing
            // out of here.
            var given =
                result.GetResult(scheduleOption) is not null
                || result.GetResult(hourOption) is not null
                || result.GetResult(minuteOption) is not null
                || result.GetResult(weekdayOption) is not null
                || result.GetResult(retentionCountOption) is not null
                || result.GetResult(retentionGbOption) is not null
                || result.GetResult(dirOption) is not null;
            if (!given)
                result.AddError("Pass at least one field to set.");
        });
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "A partial update despite the PUT: omitted fields are left alone. Echoes",
            "the full effective settings.",
            "",
            "--weekday is 0=Mon … 6=Sun, and applies only to --schedule weekly.",
            "",
            "A --dir path is checked for writability now, not at the next scheduled",
            "run.",
            "",
            "--schedule, --retention-count, --retention-gb and --dir are 400 when an",
            "environment variable pins them; settings get reports which.",
            "",
            "Out-of-range numbers are rejected here rather than sent: the server",
            "clamps them and answers 200, so a typo would otherwise be stored as a",
            "different value.");
        command.AddExamples(
            "grimoire-cli backups settings set --schedule daily --hour 3",
            "grimoire-cli backups settings set --retention-count 7 --retention-gb 20",
            "grimoire-cli backups settings set --dir \"\"");
        command.AddResponseExample<Generated.Models.BackupSettingsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new BackupsService(client);
            var result = await service.UpdateSettingsAsync(
                parseResult.GetValue(scheduleOption),
                parseResult.GetValue(hourOption),
                parseResult.GetValue(minuteOption),
                parseResult.GetValue(weekdayOption),
                parseResult.GetValue(retentionCountOption),
                parseResult.GetValue(retentionGbOption),
                parseResult.GetValue(dirOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
