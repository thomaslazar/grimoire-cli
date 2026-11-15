using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignGmScreenCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    internal const string OwnerNote = "Only the campaign owner; anyone else gets 403.";
    internal static readonly string[] Locked =
    [
        "409 while the campaign is archived; 403 while the GM's campaign access",
        "is disabled.",
    ];
    private static readonly string[] Scopes = ["scene", "session", "rest"];

    public static Command Create()
    {
        var command = new Command("gm-screen", "The campaign GM's board of tools");
        command.Subcommands.Add(CreateReadCommand("get", "Get the GM screen's panels", (s, id) => s.GetAsync(id), []));
        command.Subcommands.Add(CreateReadCommand("party", "The party's characters and values for the GM screen", (s, id) => s.PartyAsync(id), []));
        command.Subcommands.Add(CreateReadCommand("export", "Export the board as a document import reads", (s, id) => s.ExportAsync(id), []));
        command.Subcommands.Add(CreateReadCommand("sources", "Other campaigns you run with a board to copy", (s, id) => s.SourcesAsync(id), []));
        command.Subcommands.Add(CreateReadCommand("presets", "Browse community GM screen presets", (s, id) => s.PresetsAsync(id),
            ["403 when the server disables external installs."]));
        command.Subcommands.Add(CreateResetCommand());
        command.Subcommands.Add(CreateLayoutCommand());
        command.Subcommands.Add(CreateImportCommand());
        command.Subcommands.Add(CreateCopyCommand());
        command.Subcommands.Add(CreateApplyPresetCommand());
        command.Subcommands.Add(CampaignGmPanelsCommands.Create());
        return command;
    }

    private static Option<string> CampaignIdOption() =>
        new("--id") { Description = "Campaign ID", Required = true };

    private static Option<bool> AppendOption() =>
        new("--append") { Description = "Add below the existing panels instead of replacing them" };

    private static string[] Notes(string[] locked, params string[] more) =>
        [OwnerNote, .. Section(locked), .. Section(more)];

    private static string[] Section(string[] lines) => lines.Length > 0 ? ["", .. lines] : [];

    private static readonly string[] AppendNotes =
    [
        "Replaces the board unless --append, which adds the panels below it. A",
        "board holds at most 60 panels; over that is a 400.",
    ];

    private static Command CreateReadCommand(string name, string description, Func<CampaignGmScreenService, string, Task<string>> call, string[] more)
    {
        var idOption = CampaignIdOption();
        var command = new Command(name, description) { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, Notes([], more));
        command.AddExamples($"grimoire-cli campaigns gm-screen {name} --id <campaign-id>");
        switch (name)
        {
            case "get": command.AddResponseExample<Generated.Models.GmScreenOut>(); break;
            case "party": command.AddResponseExample<Generated.Models.GmPartyOut>(); break;
            case "export": command.AddResponseExample<Generated.Models.GmBoardExport>(); break;
            case "sources": command.AddResponseExample<Generated.Models.GmSourcesOut>(); break;
            case "presets": command.AddResponseExample<Generated.Models.GmPresetListOut>(); break;
        }
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await call(new CampaignGmScreenService(client), parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateResetCommand()
    {
        var idOption = CampaignIdOption();
        var scopeOption = OptionHelpers.Choice("--scope", "What is ending", Scopes);
        scopeOption.Required = true;
        var command = new Command("reset", "End a scene or session, or rest: clear marked panels") { idOption, scopeOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            Notes(Locked,
                "Clears what each panel is marked to clear on that scope (its resets);",
                "session also clears what clears with scene."));
        command.AddExamples("grimoire-cli campaigns gm-screen reset --id <campaign-id> --scope scene");
        command.AddResponseExample<Generated.Models.GmScreenOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignGmScreenService(client).ResetAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(scopeOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateLayoutCommand()
    {
        var idOption = CampaignIdOption();
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("layout", "Move and resize several panels at once") { idOption, inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            Notes(Locked, "An id not on the board is skipped."));
        command.AddExamples("echo '{\"panels\":[{\"id\":\"<panel-id>\",\"x\":0,\"y\":0,\"w\":4,\"h\":3}]}' | grimoire-cli campaigns gm-screen layout --id <campaign-id> --stdin");
        command.AddRequestShape<Generated.Models.GmLayoutUpdate>();
        command.AddResponseExample<Generated.Models.GmScreenOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.GmLayoutUpdate.CreateFromDiscriminatorValue, "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignGmScreenService(client).LayoutAsync(parseResult.GetValue(idOption)!, body));
            return 0;
        });
        return command;
    }

    private static Command CreateImportCommand()
    {
        var idOption = CampaignIdOption();
        var inputOption = new Option<string?>("--input") { Description = "Read the document from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the document from stdin" };
        var appendOption = AppendOption();
        var command = new Command("import", "Put a board document on the GM screen") { idOption, inputOption, stdinOption, appendOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            Notes(Locked,
                [.. AppendNotes, "",
                "Takes the document export writes; an empty panels list or an unknown",
                "kind is a 400."]));
        command.AddExamples("grimoire-cli campaigns gm-screen export --id <campaign-id> | grimoire-cli campaigns gm-screen import --id <other-id> --stdin");
        command.AddResponseExample<Generated.Models.GmScreenOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string document;
            try
            {
                document = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                CampaignGmScreenService.BuildImportBody(document, false);
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignGmScreenService(client).ImportAsync(
                parseResult.GetValue(idOption)!, document, parseResult.GetValue(appendOption)));
            return 0;
        });
        return command;
    }

    private static Command CreateCopyCommand()
    {
        var idOption = CampaignIdOption();
        var sourceOption = new Option<string>("--source-id") { Description = "Campaign to copy the board from", Required = true };
        var appendOption = AppendOption();
        var command = new Command("copy", "Copy another campaign's board") { idOption, sourceOption, appendOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            Notes(Locked,
                [.. AppendNotes, "",
                "--source-id from sources. Copies the setup, not the values: counters",
                "back to their start, clocks unticked, initiative empty; notes match by",
                "page slug. A campaign you don't own, or this one, is a 404; an empty",
                "board a 400."]));
        command.AddExamples("grimoire-cli campaigns gm-screen copy --id <campaign-id> --source-id <source-id>");
        command.AddResponseExample<Generated.Models.GmScreenOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignGmScreenService(client).CopyAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(sourceOption)!, parseResult.GetValue(appendOption)));
            return 0;
        });
        return command;
    }

    private static Command CreateApplyPresetCommand()
    {
        var idOption = CampaignIdOption();
        var presetOption = new Option<string>("--preset-id") { Description = "Preset ID", Required = true };
        var indexUrlOption = new Option<string?>("--index-url") { Description = "The preset's index_url" };
        var appendOption = AppendOption();
        var command = new Command("apply-preset", "Apply a community GM screen preset") { idOption, presetOption, indexUrlOption, appendOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            Notes(Locked,
                [.. AppendNotes, "",
                "--preset-id and --index-url from presets. 403 when the server disables",
                "external installs; 404 for an unknown preset; 502 when the download",
                "fails."]));
        command.AddExamples("grimoire-cli campaigns gm-screen apply-preset --id <campaign-id> --preset-id <preset-id>");
        command.AddResponseExample<Generated.Models.GmScreenOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignGmScreenService(client).ApplyPresetAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(presetOption)!,
                parseResult.GetValue(indexUrlOption), parseResult.GetValue(appendOption)));
            return 0;
        });
        return command;
    }
}
