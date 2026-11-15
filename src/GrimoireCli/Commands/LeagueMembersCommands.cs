using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class LeagueMembersCommands
{
    public static Command Create()
    {
        var command = new Command("members", "Who plays in a league");
        command.Subcommands.Add(CreateAddCommand());
        command.Subcommands.Add(CreateRemoveCommand());
        command.Subcommands.Add(CreateArmyCommand());
        return command;
    }

    private static Option<string> LeagueIdOption() =>
        new("--id") { Description = "League ID", Required = true };

    private static Option<string> UserIdOption() =>
        new("--user-id") { Description = "User ID (from leagues eligible or the league's members)", Required = true };

    private static Command CreateAddCommand()
    {
        var idOption = LeagueIdOption();
        var userIdOption = UserIdOption();
        var command = new Command("add", "Add a user to the league") { idOption, userIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Organiser or admin only. An unknown user or a guest is a 404; adding an",
            "existing member again is a no-op.");
        command.AddExamples("grimoire-cli leagues members add --id <league-id> --user-id <user-id>");
        command.AddResponseExample<Generated.Models.LeagueDetail>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeagueMembersService(client).AddAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(userIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateRemoveCommand()
    {
        var idOption = LeagueIdOption();
        var userIdOption = UserIdOption();
        var command = new Command("remove", "Remove a member, or leave") { idOption, userIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Your own id leaves the league; anyone else is organiser or admin only.",
            "Their games stay, and they drop out of the standings.");
        command.AddExamples("grimoire-cli leagues members remove --id <league-id> --user-id <user-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeagueMembersService(client).RemoveAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(userIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateArmyCommand()
    {
        var idOption = LeagueIdOption();
        var userIdOption = UserIdOption();
        var armyOption = new Option<string?>("--army-id") { Description = "Army ID; omit to clear" };
        var command = new Command("army", "Register the army a member plays") { idOption, userIdOption, armyOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Your own registration, or anyone's as organiser or admin. The army must",
            "belong to that member; an army that is not theirs is a 400. They must",
            "already be a member (404). Omitting --army-id clears it. A registered",
            "army becomes readable to the whole league.");
        command.AddExamples(
            "grimoire-cli leagues members army --id <league-id> --user-id <user-id> --army-id <army-id>",
            "grimoire-cli leagues members army --id <league-id> --user-id <user-id>");
        command.AddResponseExample<Generated.Models.LeagueDetail>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeagueMembersService(client).ArmyAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(userIdOption)!, parseResult.GetValue(armyOption)));
            return 0;
        });
        return command;
    }
}
