using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class LeaguesCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("leagues", "Wargame leagues");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateEligibleCommand());
        command.Subcommands.Add(LeagueMembersCommands.Create());
        return command;
    }

    private static Option<string> LeagueIdOption() =>
        new("--id") { Description = "League ID", Required = true };

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List your leagues");
        command.AddHelpSection("Notes", HelpSectionPosition.Top, "Leagues you organise or belong to; an admin sees every league.");
        command.AddExamples("grimoire-cli leagues list");
        command.AddResponseExampleArray<Generated.Models.LeagueSummary>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeaguesService(client).ListAsync());
            return 0;
        });
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = LeagueIdOption();
        var command = new Command("get", "Get a league with its members, games and standings") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, "Not being in the league is a 404.");
        command.AddExamples("grimoire-cli leagues get --id <league-id>");
        command.AddResponseExample<Generated.Models.LeagueDetail>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeaguesService(client).GetAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateCreateCommand()
    {
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("create", "Create a league you organise") { inputOption, stdinOption };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The organiser is not a member until added, so is not in the standings and",
            "cannot be a game player. points_limit is stored but never enforced.");
        command.AddExamples(
            "echo '{\"name\":\"Autumn Escalation\",\"points_limit\":1500}' | grimoire-cli leagues create --stdin");
        command.AddRequestShape<Generated.Models.LeagueCreate>();
        command.AddResponseExample<Generated.Models.LeagueDetail>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.LeagueCreate.CreateFromDiscriminatorValue, "it is assigned by the server");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeaguesService(client).CreateAsync(body));
            return 0;
        });
        return command;
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = LeagueIdOption();
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("update", "Update a league") { idOption, inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Organiser or admin only. Partial: send only the fields to change.",
            "status \"finished\" is only a label. Changing the win, draw or loss",
            "points re-scores the standings.");
        command.AddExamples(
            "echo '{\"status\":\"finished\"}' | grimoire-cli leagues update --id <league-id> --stdin");
        command.AddRequestShape<Generated.Models.LeagueUpdate>();
        command.AddResponseExample<Generated.Models.LeagueDetail>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.LeagueUpdate.CreateFromDiscriminatorValue, "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeaguesService(client).UpdateAsync(parseResult.GetValue(idOption)!, body));
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = LeagueIdOption();
        var command = new Command("delete", "Delete a league") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Organiser or admin only. Removes the shares, games and memberships;",
            "armies and guest accounts stay.");
        command.AddExamples("grimoire-cli leagues delete --id <league-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeaguesService(client).DeleteAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateEligibleCommand()
    {
        var idOption = LeagueIdOption();
        var command = new Command("eligible", "Users who can be added") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, "Organiser or admin only. Full accounts that are not yet members.");
        command.AddExamples("grimoire-cli leagues eligible --id <league-id>");
        command.AddResponseExampleArray<Generated.Models.EligibleMember>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeaguesService(client).EligibleAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }
}
