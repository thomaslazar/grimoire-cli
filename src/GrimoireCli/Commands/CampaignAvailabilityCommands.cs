using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignAvailabilityCommands
{
    public static Command Create()
    {
        var command = new Command("availability", "Who can make the upcoming sessions");
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateSetCommand());
        command.Subcommands.Add(CreateToggleCancelCommand());
        return command;
    }

    private static Option<string> CampaignIdOption() =>
        new("--id") { Description = "Campaign ID", Required = true };

    private static Option<string> DateOption(string description) =>
        new("--date") { Description = description, Required = true };

    private static Command CreateGetCommand()
    {
        var idOption = CampaignIdOption();
        var command = new Command("get", "Each member's availability for the next 10 sessions") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, ["Any campaign member. A date is cancelled when any row for it is."]);
        command.AddExamples("grimoire-cli campaigns availability get --id <campaign-id>");
        command.AddResponseExample<Generated.Models.AvailabilityOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignAvailabilityService(client).GetAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateSetCommand()
    {
        var idOption = CampaignIdOption();
        var dateOption = DateOption("Session date, YYYY-MM-DD");
        var statusOption = OptionHelpers.Choice("--status", "Your answer for that date", ["available", "tentative", "unavailable"]);
        statusOption.Required = true;
        var cancelledOption = new Option<bool?>("--is-cancelled") { Description = "Mark the date cancelled on the target's row (true | false)" };
        var userIdOption = new Option<string?>("--user-id") { Description = "Set another member's row instead of your own" };
        var command = new Command("set", "Set availability for one session date") { idOption, dateOption, statusOption, cancelledOption, userIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            ["Any campaign member, for their own row; refused while the campaign is",
            "archived. Any valid date is accepted, scheduled or not.",
            "",
            "--is-cancelled needs the owner or an admin and sets only the target's row,",
            "so it cannot reliably uncancel a date toggle-cancel cancelled.",
            "--user-id answers 422 for any real user id: the server types user_id as",
            "an integer, and user ids are strings."]);
        command.AddExamples("grimoire-cli campaigns availability set --id <campaign-id> --date 2026-10-21 --status tentative");
        command.AddResponseExample<Generated.Models.AvailabilitySetOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignAvailabilityService(client).SetAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(dateOption)!, parseResult.GetValue(statusOption)!,
                parseResult.GetValue(cancelledOption), parseResult.GetValue(userIdOption)));
            return 0;
        });
        return command;
    }

    private static Command CreateToggleCancelCommand()
    {
        var idOption = CampaignIdOption();
        var dateOption = DateOption("Session date, YYYY-MM-DD");
        var command = new Command("toggle-cancel", "Flip whether a session date is cancelled") { idOption, dateOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            ["Campaign owner or an admin; refused while the campaign is archived.",
            "",
            "Sets every member's row to the opposite of your own row's state, so a",
            "second call undoes it; is_cancelled in the response is the new state. With",
            "no answer of your own for the date, it records you as available."]);
        command.AddExamples("grimoire-cli campaigns availability toggle-cancel --id <campaign-id> --date 2026-10-21");
        command.AddResponseExample<Generated.Models.SessionDateCancelOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignAvailabilityService(client).ToggleCancelAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(dateOption)!));
            return 0;
        });
        return command;
    }
}
