using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignScheduleCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("schedule", "A group campaign's recurring session schedule");
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateSetCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        return command;
    }

    private static Option<string> CampaignIdOption() =>
        new("--id") { Description = "Campaign ID", Required = true };

    private static Command CreateGetCommand()
    {
        var idOption = CampaignIdOption();
        var command = new Command("get", "Get the schedule and the next 10 session dates") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, ["Any campaign member. definition is null when none is set."]);
        command.AddExamples("grimoire-cli campaigns schedule get --id <campaign-id>");
        command.AddResponseExample<Generated.Models.ScheduleOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignScheduleService(client).GetAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateSetCommand()
    {
        var idOption = CampaignIdOption();
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("set", "Create or replace the schedule") { idOption, inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            ["Campaign owner only; refused while the campaign is archived. Group",
            "campaigns only (400 on a personal one).",
            "",
            "Replaces the whole schedule; omitted fields take their defaults. days are",
            "0 (Monday) to 6 (Sunday) and time_local is HH:MM, both in timezone (an IANA",
            "name). biweekly uses biweekly_reference (YYYY-MM-DD, default today);",
            "monthly uses monthly_week (1-4, -1 = last, default 1); custom uses only",
            "custom_dates. time_utc is a legacy alias. enabled false keeps the",
            "definition but yields no sessions. next_sessions counts from yesterday."]);
        command.AddExamples(
            "echo '{\"frequency\":\"weekly\",\"days\":[2],\"time_local\":\"19:30\",\"timezone\":\"Europe/Berlin\"}' | grimoire-cli campaigns schedule set --id <campaign-id> --stdin");
        command.AddRequestShape<Generated.Models.ScheduleUpsert>();
        command.AddResponseExample<Generated.Models.ScheduleOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.ScheduleUpsert.CreateFromDiscriminatorValue, "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignScheduleService(client).SetAsync(parseResult.GetValue(idOption)!, body));
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = CampaignIdOption();
        var command = new Command("delete", "Remove the schedule") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, ["Campaign owner only; refused while the campaign is archived."]);
        command.AddExamples("grimoire-cli campaigns schedule delete --id <campaign-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignScheduleService(client).DeleteAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }
}
