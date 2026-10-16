using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignMemberSheetCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("sheet", "A member's character sheet file");
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateUploadCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateFromSourceCommand());
        command.Subcommands.Add(CreateSourcesCommand());
        return command;
    }

    private static Option<string> IdOption() => new("--id") { Description = "Campaign ID", Required = true };

    private static Option<string> MemberIdOption() =>
        new("--member-id") { Description = "Member ID: id from campaigns get members[] or guests list, not user_id", Required = true };

    private static Command CreateGetCommand()
    {
        var idOption = IdOption();
        var memberIdOption = MemberIdOption();
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for the file to stdout",
            Required = true,
        };
        var command = new Command("get", "Download the character sheet")
        {
            idOption, memberIdOption, outputOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Any campaign member. 404 when the slot is empty.",
            "The file keeps the name it was uploaded with.",
            "",
            "--output - writes the bytes to stdout; a path writes the file and prints",
            "{path, bytes}.");
        command.AddExamples("grimoire-cli campaigns members sheet get --id <campaign-id> --member-id <member-id> --output sheet.pdf");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            await using var stream = await new CampaignMemberSheetService(client).GetAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(memberIdOption)!);
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

    private static Command CreateUploadCommand()
    {
        var idOption = IdOption();
        var memberIdOption = MemberIdOption();
        var fileOption = new Option<string>("--file") { Description = "Path to a PDF or image", Required = true };
        var command = new Command("upload", "Upload the character sheet")
        {
            idOption, memberIdOption, fileOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The member themselves or the campaign owner; 409 while the campaign is",
            "archived. The owner has no slot of their own.",
            "",
            "A PDF, or a PNG, JPEG, WebP or GIF, max 15 MB; replaces the current",
            "sheet and clears any sheet link. A link set with members update clears",
            "the uploaded file in turn.");
        command.AddExamples("grimoire-cli campaigns members sheet upload --id <campaign-id> --member-id <member-id> --file sheet.pdf");
        command.AddResponseExample<Generated.Models.MemberSheetOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            string result;
            try
            {
                result = await new CampaignMemberSheetService(client).UploadAsync(
                    parseResult.GetValue(idOption)!, parseResult.GetValue(memberIdOption)!, parseResult.GetValue(fileOption)!);
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = IdOption();
        var memberIdOption = MemberIdOption();
        var command = new Command("delete", "Remove the character sheet")
        {
            idOption, memberIdOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The member themselves or the campaign owner; 409 while the campaign is",
            "archived. The owner has no slot of their own.");
        command.AddExamples("grimoire-cli campaigns members sheet delete --id <campaign-id> --member-id <member-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignMemberSheetService(client).DeleteAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(memberIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateFromSourceCommand()
    {
        var idOption = IdOption();
        var memberIdOption = MemberIdOption();
        var sourceTypeOption = OptionHelpers.Choice("--source-type", "Where the blank sheet comes from", ["book", "file"]);
        sourceTypeOption.Required = true;
        var sourceIdOption = new Option<string>("--source-id") { Description = "Its ID, from sheet sources", Required = true };
        var command = new Command("from-source", "Copy a blank sheet PDF into the member's slot")
        {
            idOption, memberIdOption, sourceTypeOption, sourceIdOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The member themselves or the campaign owner; 409 while the campaign is",
            "archived. The owner has no slot of their own.",
            "",
            "Only what sheet sources lists for you can be copied (404 otherwise); PDF",
            "only, max 15 MB. Clears any sheet link.");
        command.AddExamples("grimoire-cli campaigns members sheet from-source --id <campaign-id> --member-id <member-id> --source-type book --source-id <book-id>");
        command.AddResponseExample<Generated.Models.MemberSheetOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignMemberSheetService(client).FromSourceAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(memberIdOption)!,
                parseResult.GetValue(sourceTypeOption)!, parseResult.GetValue(sourceIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateSourcesCommand()
    {
        var idOption = IdOption();
        var command = new Command("sources", "Blank sheets you can copy into a slot") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Any campaign member. Library books in the character-sheet category that",
            "you can read, limited to the campaign's system when it has one, and",
            "campaign PDF files you can read.");
        command.AddExamples("grimoire-cli campaigns members sheet sources --id <campaign-id>");
        command.AddResponseExample<Generated.Models.SheetSourcesOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignMemberSheetService(client).SourcesAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }
}
