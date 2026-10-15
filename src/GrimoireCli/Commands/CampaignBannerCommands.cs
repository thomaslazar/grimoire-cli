using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignBannerCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private const string Owner = "Campaign owner only; refused while the campaign is archived.";

    public static Command Create()
    {
        var command = new Command("banner", "The campaign's header image");
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateUploadCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateFromSourceCommand());
        command.Subcommands.Add(CreateFocusCommand());
        return command;
    }

    private static Option<string> IdOption() => new("--id") { Description = "Campaign ID", Required = true };

    private static Command CreateGetCommand()
    {
        var idOption = IdOption();
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for the image to stdout",
            Required = true,
        };
        var sizeOption = OptionHelpers.Choice("--size", "Image size; default small", ["small", "full"]);
        var command = new Command("get", "Download the campaign banner")
        {
            idOption, outputOption, sizeOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Any campaign member.",
            "",
            "small is a WebP at most 1000 px wide; full is the original. 404 when no",
            "banner is set.",
            "",
            "--output - writes the image to stdout; a path writes the file and prints",
            "{path, bytes}.");
        command.AddExamples(
            "grimoire-cli campaigns banner get --id <campaign-id> --output banner.webp",
            "grimoire-cli campaigns banner get --id <campaign-id> --size full --output - > banner.png");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new CampaignBannerService(client);
            await using var stream = await service.GetAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(sizeOption) == "full");
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
        var fileOption = new Option<string>("--file") { Description = "Path to a PNG, JPEG, WebP or GIF", Required = true };
        var command = new Command("upload", "Upload the campaign banner")
        {
            idOption, fileOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            Owner,
            "",
            "PNG, JPEG, WebP or GIF, max 5 MB; replaces any banner.");
        command.AddExamples("grimoire-cli campaigns banner upload --id <campaign-id> --file banner.png");
        command.AddResponseExample<Generated.Models.BannerUploadOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            string result;
            try
            {
                result = await new CampaignBannerService(client).UploadAsync(
                    parseResult.GetValue(idOption)!, parseResult.GetValue(fileOption)!);
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
        var command = new Command("delete", "Remove the campaign banner")
        {
            idOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            Owner,
            "",
            "Also resets focus to 50.");
        command.AddExamples("grimoire-cli campaigns banner delete --id <campaign-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new CampaignBannerService(client).DeleteAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateFromSourceCommand()
    {
        var idOption = IdOption();
        var sourceTypeOption = OptionHelpers.Choice("--source-type", "The kind of item to copy the image from",
            ["map", "token", "book", "audio", "campaign_file"]);
        sourceTypeOption.Required = true;
        var sourceIdOption = new Option<string>("--source-id") { Description = "That item's ID", Required = true };
        var command = new Command("from-source", "Set the banner from an image Grimoire already holds")
        {
            idOption, sourceTypeOption, sourceIdOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            Owner,
            "",
            "campaign_file takes a file id from this campaign (resource_id of a file",
            "resource). You must be able to read the source yourself. The bytes are",
            "copied as WebP, so the banner outlives the source.");
        command.AddExamples(
            "grimoire-cli campaigns banner from-source --id <campaign-id> --source-type map --source-id <map-id>");
        command.AddResponseExample<Generated.Models.BannerUploadOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new CampaignBannerService(client).FromSourceAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(sourceTypeOption)!,
                parseResult.GetValue(sourceIdOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateFocusCommand()
    {
        var idOption = IdOption();
        var focusYOption = new Option<int>("--focus-y") { Description = "Vertical focal point, 0 (top) to 100 (bottom)", Required = true };
        var command = new Command("focus", "Set where the banner sits in its 2:1 frame")
        {
            idOption, focusYOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            Owner,
            "",
            "50 centres it; out of range is a 422.");
        command.AddExamples("grimoire-cli campaigns banner focus --id <campaign-id> --focus-y 30");
        command.AddResponseExample<Generated.Models.BannerFocusOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new CampaignBannerService(client).FocusAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(focusYOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
