using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignMemberTokenCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("token", "A member's character token");
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateUploadCommand());
        command.Subcommands.Add(CreateDeleteCommand());
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
            Description = "Output file path, or '-' for the image to stdout",
            Required = true,
        };
        var command = new Command("get", "Download the character token")
        {
            idOption, memberIdOption, outputOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Any campaign member. 404 when the slot is empty.",
            "",
            "--output - writes the bytes to stdout; a path writes the file and prints",
            "{path, bytes}.");
        command.AddExamples("grimoire-cli campaigns members token get --id <campaign-id> --member-id <member-id> --output token.png");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            await using var stream = await new CampaignMemberTokenService(client).GetAsync(
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
        var fileOption = new Option<string>("--file") { Description = "Path to a PNG, JPEG, WebP or GIF", Required = true };
        var command = new Command("upload", "Upload the character token")
        {
            idOption, memberIdOption, fileOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The member themselves or the campaign owner; 409 while the campaign is",
            "archived. The owner has no slot of their own.",
            "",
            "PNG, JPEG, WebP or GIF, max 5 MB; replaces the current image.");
        command.AddExamples("grimoire-cli campaigns members token upload --id <campaign-id> --member-id <member-id> --file token.png");
        command.AddResponseExample<Generated.Models.MemberTokenUploadOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            string result;
            try
            {
                result = await new CampaignMemberTokenService(client).UploadAsync(
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
        var command = new Command("delete", "Remove the character token")
        {
            idOption, memberIdOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The member themselves or the campaign owner; 409 while the campaign is",
            "archived. The owner has no slot of their own.");
        command.AddExamples("grimoire-cli campaigns members token delete --id <campaign-id> --member-id <member-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignMemberTokenService(client).DeleteAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(memberIdOption)!));
            return 0;
        });
        return command;
    }
}
