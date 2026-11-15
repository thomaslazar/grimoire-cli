using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class LeagueGuestsCommands
{
    private const string Organiser = "Organiser or admin only.";
    private const string Code = "The code is the guest's login; anyone holding it is that guest.";

    public static Command Create()
    {
        var command = new Command("guests", "Guest invite codes for players without an account");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateRegenerateCommand());
        command.Subcommands.Add(CreateShareCommand());
        command.Subcommands.Add(CreateRemoveCommand());
        return command;
    }

    private static Option<string> LeagueIdOption() =>
        new("--id") { Description = "League ID", Required = true };

    private static Option<string> UserIdOption() =>
        new("--user-id") { Description = "Guest's user ID (user_id from guests list)", Required = true };

    private static Command CreateListCommand()
    {
        var idOption = LeagueIdOption();
        var command = new Command("list", "List the league's guests and their codes") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [Organiser, "guest_code is null unless you organise every league the guest is in."]);
        command.AddExamples("grimoire-cli leagues guests list --id <league-id>");
        command.AddResponseExampleArray<Generated.Models.LeagueGuest>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeagueGuestsService(client).ListAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateCreateCommand()
    {
        var idOption = LeagueIdOption();
        var nicknameOption = new Option<string?>("--nickname") { Description = "Display name for the guest (default Guest)" };
        var command = new Command("create", "Create a guest and their invite code") { idOption, nicknameOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [Organiser, "403 when the server has guest access off.", Code]);
        command.AddExamples("grimoire-cli leagues guests create --id <league-id> --nickname \"Sam\"");
        command.AddResponseExample<Generated.Models.LeagueGuest>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeagueGuestsService(client).CreateAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(nicknameOption)));
            return 0;
        });
        return command;
    }

    private static Command CreateRegenerateCommand()
    {
        var idOption = LeagueIdOption();
        var userIdOption = UserIdOption();
        var command = new Command("regenerate", "Replace a guest's invite code") { idOption, userIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [Organiser, "403 when the server has guest access off. The old code stops working",
            "and the guest's sessions end. A guest who also plays in another organiser's",
            "league is a 403.", Code]);
        command.AddExamples("grimoire-cli leagues guests regenerate --id <league-id> --user-id <user-id>");
        command.AddResponseExample<Generated.Models.LeagueGuest>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeagueGuestsService(client).RegenerateAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(userIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateShareCommand()
    {
        var idOption = LeagueIdOption();
        var userIdOption = UserIdOption();
        var command = new Command("share", "Invite text and link carrying a guest's code") { idOption, userIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [Organiser, Code, "The link is built from the server's BASE_URL."]);
        command.AddExamples("grimoire-cli leagues guests share --id <league-id> --user-id <user-id>");
        command.AddResponseExample<Generated.Models.GuestShareTemplate>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeagueGuestsService(client).ShareAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(userIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateRemoveCommand()
    {
        var idOption = LeagueIdOption();
        var userIdOption = UserIdOption();
        var command = new Command("remove", "Remove a guest from the league") { idOption, userIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [Organiser, "The guest only leaves the league: the account, their armies and",
            "their sign-in remain, unlike campaigns guests remove."]);
        command.AddExamples("grimoire-cli leagues guests remove --id <league-id> --user-id <user-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeagueGuestsService(client).RemoveAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(userIdOption)!));
            return 0;
        });
        return command;
    }
}
