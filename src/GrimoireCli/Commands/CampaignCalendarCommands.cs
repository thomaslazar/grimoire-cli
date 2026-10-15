using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignCalendarCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private const string SessionOnly = "Refused with an API-key login: these routes need a password login.";

    public static Command Create()
    {
        var command = new Command("calendar", "Campaign sessions as calendar (.ics) data");
        command.Subcommands.Add(CreateDownloadCommand());
        command.Subcommands.Add(CreateSubscriptionCommand());
        return command;
    }

    private static Command CreateDownloadCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for the calendar to stdout",
            Required = true,
        };
        var command = new Command("download", "Download the next 26 sessions as an .ics file") { idOption, outputOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Any campaign member. Event titles carry your own availability; cancelled",
            "dates are marked. Needs no feed token.",
            "",
            "--output - writes the calendar to stdout; a path writes the file and",
            "prints {path, bytes}.");
        command.AddExamples("grimoire-cli campaigns calendar download --id <campaign-id> --output sessions.ics");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            await using var stream = await new CampaignCalendarService(client).DownloadAsync(parseResult.GetValue(idOption)!);
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

    private static Command CreateSubscriptionCommand()
    {
        var command = new Command("subscription", "Your calendar-feed token and its URLs");
        command.Subcommands.Add(CreateSubscriptionGetCommand());
        command.Subcommands.Add(CreateSubscriptionCreateCommand());
        command.Subcommands.Add(CreateSubscriptionRevokeCommand());
        return command;
    }

    private static Option<string?> OptionalIdOption() => new("--id") { Description = "Also return this campaign's feed URL" };

    private static Command CreateSubscriptionGetCommand()
    {
        var idOption = OptionalIdOption();
        var command = new Command("get", "Show your calendar-feed URLs") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, SessionOnly);
        command.AddExamples("grimoire-cli campaigns calendar subscription get --id <campaign-id>");
        command.AddResponseExample<Generated.Models.CalendarSubscriptionOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignCalendarService(client).SubscriptionAsync(parseResult.GetValue(idOption)));
            return 0;
        });
        return command;
    }

    private static Command CreateSubscriptionCreateCommand()
    {
        var idOption = OptionalIdOption();
        var command = new Command("create", "Mint or rotate your calendar-feed token") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            SessionOnly,
            "",
            "Rotating breaks every URL issued before. 400 unless the server's BASE_URL",
            "is set.");
        command.AddExamples("grimoire-cli campaigns calendar subscription create");
        command.AddResponseExample<Generated.Models.CalendarSubscriptionOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignCalendarService(client).CreateSubscriptionAsync(parseResult.GetValue(idOption)));
            return 0;
        });
        return command;
    }

    private static Command CreateSubscriptionRevokeCommand()
    {
        var command = new Command("revoke", "Revoke your calendar-feed token");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            SessionOnly,
            "",
            "Every subscribed URL stops working.");
        command.AddExamples("grimoire-cli campaigns calendar subscription revoke");
        command.AddResponseExample<Generated.Models.CalendarSubscriptionOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignCalendarService(client).RevokeSubscriptionAsync());
            return 0;
        });
        return command;
    }
}
