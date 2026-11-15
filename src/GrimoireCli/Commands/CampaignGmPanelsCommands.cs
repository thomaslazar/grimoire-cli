using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignGmPanelsCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("panels", "Panels on the GM screen");
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        return command;
    }

    private static Option<string> CampaignIdOption() =>
        new("--id") { Description = "Campaign ID", Required = true };

    private static Option<string> PanelIdOption() =>
        new("--panel-id") { Description = "Panel ID, from gm-screen get", Required = true };

    private static string[] Notes(params string[] more) =>
        [CampaignGmScreenCommands.OwnerNote, .. CampaignGmScreenCommands.Locked, "", .. more];

    private static Command CreateCreateCommand()
    {
        var idOption = CampaignIdOption();
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("create", "Add a panel to the GM screen") { idOption, inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        GmPanelHelp.AddTo(command);
        command.AddHelpSection("Notes", HelpSectionPosition.Top, Notes("A board holds at most 60 panels."));
        command.AddExamples("echo '{\"kind\":\"counter\",\"title\":\"Fear\",\"config\":{\"min\":0,\"max\":12,\"resets\":\"session\"}}' | grimoire-cli campaigns gm-screen panels create --id <campaign-id> --stdin");
        command.AddRequestShape<Generated.Models.GmPanelCreate>();
        command.AddResponseExample<Generated.Models.GmPanelOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.GmPanelCreate.CreateFromDiscriminatorValue, "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignGmPanelsService(client).CreateAsync(parseResult.GetValue(idOption)!, body));
            return 0;
        });
        return command;
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = CampaignIdOption();
        var panelIdOption = PanelIdOption();
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("update", "Update a GM screen panel") { idOption, panelIdOption, inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        GmPanelHelp.AddTo(command);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            Notes("Only the fields given change. config and state each replace whole: send",
                "every key you want kept; a new config re-cleans the state against it.",
                "An unknown panel id is a 404."));
        command.AddExamples("echo '{\"state\":{\"value\":3}}' | grimoire-cli campaigns gm-screen panels update --id <campaign-id> --panel-id <panel-id> --stdin");
        command.AddRequestShape<Generated.Models.GmPanelUpdate>();
        command.AddResponseExample<Generated.Models.GmPanelOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.GmPanelUpdate.CreateFromDiscriminatorValue, "pass it with --id and --panel-id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignGmPanelsService(client).UpdateAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(panelIdOption)!, body));
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = CampaignIdOption();
        var panelIdOption = PanelIdOption();
        var command = new Command("delete", "Remove a GM screen panel") { idOption, panelIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, Notes("An unknown panel id is a 404."));
        command.AddExamples("grimoire-cli campaigns gm-screen panels delete --id <campaign-id> --panel-id <panel-id>");
        command.AddResponseExample<Generated.Models.GmScreenOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignGmPanelsService(client).DeleteAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(panelIdOption)!));
            return 0;
        });
        return command;
    }
}
