using System.CommandLine;
using GrimoireCli.Api;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

/// <summary>
/// The detection and dismissal leaves of the `duplicates` group. Split from
/// DuplicatesCommand for the same reason FilesFolderCommands is split from
/// FilesCommand: thirteen leaves in one file would be the largest command file
/// in the repo by half.
/// </summary>
public static class DuplicatesScanCommands
{
    public static IEnumerable<Command> Create() =>
    [
        CreateScanCommand(),
        CreateScanStatusCommand(),
        CreateCancelScanCommand(),
        CreateGroupsCommand(),
        CreateDismissCommand(),
        CreateDismissalsCommand(),
        CreateUndismissCommand(),
    ];

    private static Command CreateScanCommand()
    {
        var resourceTypesOption = new Option<string[]>("--resource-types")
        {
            Description = "Collections to scan; repeatable",
            AllowMultipleArgumentsPerToken = true,
        };
        resourceTypesOption.Validators.Add(result =>
        {
            foreach (var value in result.GetValueOrDefault<string[]>() ?? [])
            {
                if (!DuplicatesCommand.ResourceTypes.Contains(value))
                    result.AddError(
                        $"'{value}' is not a valid value for --resource-types. Must be one of: {string.Join(", ", DuplicatesCommand.ResourceTypes)}");
            }
        });
        resourceTypesOption.CompletionSources.Add(DuplicatesCommand.ResourceTypes);
        var accuracyOption = OptionHelpers.Choice(
            "--accuracy", "Detection accuracy; default medium", ["exact", "high", "medium", "low"]);
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("scan", "Start a duplicate-detection pass")
        {
            resourceTypesOption, accuracyOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Runs in the background; poll scan-status.",
            "",
            "409 while a library scan is running. A duplicate scan already in flight",
            "answers 200 with already_running and exit 3, having started nothing — a",
            "scan whose heartbeat has gone stale is discarded instead and this one",
            "starts.",
            "",
            "--accuracy trades certainty for reach: exact matches only byte-identical",
            "files and never guesses; the looser levels take longer and return matches",
            "that need judging.",
            "",
            "Omitting --resource-types scans all five.");
        command.AddExamples(
            "grimoire-cli duplicates scan",
            "grimoire-cli duplicates scan --resource-types book --accuracy exact");
        command.AddResponseExample<Generated.Models.ScanTriggerResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var result = await new DuplicatesService(client).ScanAsync(
                parseResult.GetValue(resourceTypesOption) ?? [],
                parseResult.GetValue(accuracyOption));
            ConsoleOutput.WriteRawJson(result);
            return ScanExit.CodeFor(GrimoireApiClient.ReadStringProperty(result, "status"));
        });
        return command;
    }

    private static Command CreateScanStatusCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("scan-status", "Show the duplicate scan's progress")
        {
            serverOption
        };
        command.AddRoleRequired("admin");
        command.AddExamples("grimoire-cli duplicates scan-status");
        command.AddResponseExample<Generated.Models.ScanStatus>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var result = await new DuplicatesService(client).ScanStatusAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateCancelScanCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("cancel-scan", "Stop the running duplicate scan")
        {
            serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Requests a stop rather than waiting for one; poll scan-status. Reports",
            "not_running when no scan is in flight, and cleared_stale when it cleared",
            "an abandoned one outright. Exits 0 either way.");
        command.AddExamples("grimoire-cli duplicates cancel-scan");
        command.AddResponseExample<Generated.Models.ScanTriggerResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var result = await new DuplicatesService(client).CancelScanAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateGroupsCommand()
    {
        var resourceTypeOption = OptionHelpers.Choice("--resource-type", "Collection to act on", DuplicatesCommand.ResourceTypes);
        var minConfidenceOption = new Option<double?>("--min-confidence") { Description = "Drop groups below this score" };
        var limitOption = OptionHelpers.Range("--limit", "Groups to return; default 50, max 200", 1, 200);
        var offsetOption = OptionHelpers.Range("--offset", "Groups to skip", 0);
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("groups", "List candidate duplicate groups from the last scan")
        {
            resourceTypeOption, minConfidenceOption, limitOption, offsetOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Groups whose members were deleted or already resolved are dropped and the",
            "server walks on to fill the page, so a short page means the listing is",
            "exhausted. total counts the groups walked for this page, not the table.",
            "",
            "suggested_kind and suggested_label per member are the server's guess, and",
            "are what link would take as-is.");
        command.AddExamples(
            "grimoire-cli duplicates groups",
            "grimoire-cli duplicates groups --resource-type book --min-confidence 0.8 --limit 20");
        command.AddResponseExample<Generated.Models.GroupListResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var result = await new DuplicatesService(client).GroupsAsync(
                parseResult.GetValue(resourceTypeOption),
                parseResult.GetValue(minConfidenceOption),
                parseResult.GetValue(limitOption),
                parseResult.GetValue(offsetOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDismissCommand()
    {
        var resourceTypeOption = OptionHelpers.Choice("--resource-type", "Collection to act on", DuplicatesCommand.ResourceTypes);
        resourceTypeOption.Required = true;
        var memberIdsOption = new Option<string[]>("--member-ids")
        {
            Description = "Items that are not duplicates of each other; repeatable",
            Required = true,
            AllowMultipleArgumentsPerToken = true,
        };
        var noteOption = new Option<string?>("--note") { Description = "Why they are not duplicates" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("dismiss", "Mark a group as not duplicates")
        {
            resourceTypeOption, memberIdsOption, noteOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "At least two --member-ids. Reversible with undismiss.");
        command.AddExamples(
            "grimoire-cli duplicates dismiss --resource-type book --member-ids <id> <id> --note \"different editions\"");
        command.AddResponseExample<Generated.Models.DismissalOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var result = await new DuplicatesService(client).DismissAsync(
                parseResult.GetValue(resourceTypeOption)!,
                parseResult.GetValue(memberIdsOption)!,
                parseResult.GetValue(noteOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDismissalsCommand()
    {
        var resourceTypeOption = OptionHelpers.Choice("--resource-type", "Collection to act on", DuplicatesCommand.ResourceTypes);
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("dismissals", "List dismissed groups")
        {
            resourceTypeOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddExamples("grimoire-cli duplicates dismissals");
        command.AddResponseExample<Generated.Models.DismissalListResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var result = await new DuplicatesService(client).DismissalsAsync(parseResult.GetValue(resourceTypeOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateUndismissCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Dismissal to undo, from dismissals", Required = true };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("undismiss", "Undo a dismissal, so the group can be found again")
        {
            idOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddExamples("grimoire-cli duplicates undismiss --id <dismissal-id>");
        command.AddResponseExample<Generated.Models.ScanTriggerResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var result = await new DuplicatesService(client).UndismissAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
