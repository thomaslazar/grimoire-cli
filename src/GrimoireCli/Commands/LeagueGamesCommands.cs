using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class LeagueGamesCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("games", "Games played in a league");
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        return command;
    }

    private static Option<string> LeagueIdOption() =>
        new("--id") { Description = "League ID", Required = true };

    private static Option<string> GameIdOption() =>
        new("--game-id") { Description = "Game ID (from leagues get)", Required = true };

    private static Option<string?> InputOption() =>
        new("--input") { Description = "Read the body from this file" };

    private static Option<bool> StdinOption() =>
        new("--stdin") { Description = "Read the body from stdin" };

    private static Command CreateCreateCommand()
    {
        var idOption = LeagueIdOption();
        var inputOption = InputOption();
        var stdinOption = StdinOption();
        var command = new Command("create", "Log a game") { idOption, inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Player ids are members[].user_id from leagues get. Both players must be",
            "different, current members; the organiser is one only if added. A",
            "non-organiser can only log games they played in. played_on is",
            "YYYY-MM-DD. result (a, b or draw, from A's side) is inferred from the",
            "scores when both are given and must match them; with neither scores nor",
            "result, 400.");
        command.AddExamples(
            "echo '{\"player_a_id\":\"<user-id>\",\"player_b_id\":\"<user-id>\",\"score_a\":12,\"score_b\":8,\"played_on\":\"2026-11-14\"}' | grimoire-cli leagues games create --id <league-id> --stdin");
        command.AddRequestShape<Generated.Models.GameIn>();
        command.AddResponseExample<Generated.Models.LeagueDetail>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.GameIn.CreateFromDiscriminatorValue, "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeagueGamesService(client).CreateAsync(parseResult.GetValue(idOption)!, body));
            return 0;
        });
        return command;
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = LeagueIdOption();
        var gameIdOption = GameIdOption();
        var inputOption = InputOption();
        var stdinOption = StdinOption();
        var command = new Command("update", "Edit a game") { idOption, gameIdOption, inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Organiser, admin, the recorder or either player only. Send the whole",
            "body: it replaces the whole game, and omitted fields reset. Same rules",
            "as create; a game whose player has left needs a new pairing.");
        command.AddExamples(
            "echo '{\"player_a_id\":\"<user-id>\",\"player_b_id\":\"<user-id>\",\"score_a\":12,\"score_b\":9}' | grimoire-cli leagues games update --id <league-id> --game-id <game-id> --stdin");
        command.AddRequestShape<Generated.Models.GameIn>();
        command.AddResponseExample<Generated.Models.LeagueDetail>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.GameIn.CreateFromDiscriminatorValue, "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeagueGamesService(client).UpdateAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(gameIdOption)!, body));
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = LeagueIdOption();
        var gameIdOption = GameIdOption();
        var command = new Command("delete", "Delete a game") { idOption, gameIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, "Organiser, admin, the recorder or either player only.");
        command.AddExamples("grimoire-cli leagues games delete --id <league-id> --game-id <game-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeagueGamesService(client).DeleteAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(gameIdOption)!));
            return 0;
        });
        return command;
    }
}
