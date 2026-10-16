using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignMemberArtCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("art", "A member's character art (their avatar)");
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
        var command = new Command("get", "Download the character art")
        {
            idOption, memberIdOption, outputOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Any campaign member. 404 when the slot is empty.",
            "",
            "--output - writes the bytes to stdout; a path writes the file and prints",
            "{path, bytes}.");
        command.AddExamples("grimoire-cli campaigns members art get --id <campaign-id> --member-id <member-id> --output art.png");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            await using var stream = await new CampaignMemberArtService(client).GetAsync(
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
        var command = new Command("upload", "Upload the character art")
        {
            idOption, memberIdOption, fileOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The member themselves or the campaign owner; 409 while the campaign is",
            "archived. The owner has no slot of their own.",
            "",
            "PNG, JPEG, WebP or GIF, max 5 MB; replaces the current image.");
        command.AddExamples("grimoire-cli campaigns members art upload --id <campaign-id> --member-id <member-id> --file art.png");
        command.AddResponseExample<Generated.Models.MemberArtUploadOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            string result;
            try
            {
                result = await new CampaignMemberArtService(client).UploadAsync(
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
        var command = new Command("delete", "Remove the character art")
        {
            idOption, memberIdOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The member themselves or the campaign owner; 409 while the campaign is",
            "archived. The owner has no slot of their own.");
        command.AddExamples("grimoire-cli campaigns members art delete --id <campaign-id> --member-id <member-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignMemberArtService(client).DeleteAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(memberIdOption)!));
            return 0;
        });
        return command;
    }
}
