using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignFilesCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly string[] OwnerOnly =
    [
        "Owner only, no admin override. 403 also when the owner's campaign",
        "access is disabled; 409 while the campaign is archived.",
    ];

    public static Command Create()
    {
        var command = new Command("files", "Files uploaded into a campaign");
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateUploadCommand());
        command.Subcommands.Add(CreateUploadImageCommand());
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var fileIdOption = new Option<string>("--file-id")
        {
            Description = "File ID: resource_id from files upload or a file row in resources list",
            Required = true,
        };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for the bytes to stdout",
            Required = true,
        };
        var command = new Command("get", "Download a campaign file")
        {
            idOption, fileIdOption, outputOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Any campaign member. Anyone but the owner reads only files whose link is",
            "public, or private and shared with them; gm or unlinked files are 403.",
            "",
            "--output - writes the bytes to stdout; a path writes the file and prints",
            "{path, bytes}.");
        command.AddExamples("grimoire-cli campaigns files get --id <campaign-id> --file-id <file-id> --output handout.pdf");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            await using var stream = await new CampaignFilesService(client).GetAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(fileIdOption)!);
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
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var fileOption = new Option<string>("--file") { Description = "Local file to upload", Required = true };
        var categoryIdOption = new Option<string?>("--category-id") { Description = "Resource category to file it under" };
        var newCategoryNameOption = new Option<string?>("--new-category-name") { Description = "Create a resource category and file it there" };
        var command = new Command("upload", "Upload a file into a campaign and link it")
        {
            idOption, fileOption, categoryIdOption, newCategoryNameOption
        };
        command.Validators.Add(result =>
        {
            if (result.GetValue(categoryIdOption) is not null && result.GetValue(newCategoryNameOption) is not null)
                result.AddError("Pass --category-id or --new-category-name, not both.");
        });
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. OwnerOnly, "",
            "Stored in the campaign, not the library, and linked at gm visibility.",
            "Removing the link deletes the file.",
            "",
            "Max 200 MB. An admin may set lower per-file or per-campaign limits (413)",
            "or disable uploads (403); admins are exempt."]);
        command.AddExamples("grimoire-cli campaigns files upload --id <campaign-id> --file handout.pdf --new-category-name Handouts");
        command.AddResponseExample<Generated.Models.LinkedResourceOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            string result;
            try
            {
                result = await new CampaignFilesService(client).UploadAsync(
                    parseResult.GetValue(idOption)!,
                    parseResult.GetValue(fileOption)!,
                    parseResult.GetValue(categoryIdOption),
                    parseResult.GetValue(newCategoryNameOption));
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

    private static Command CreateUploadImageCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var fileOption = new Option<string>("--file") { Description = "Local image to upload", Required = true };
        var categoryIdOption = new Option<string?>("--category-id") { Description = "Resource category to file it under" };
        var newCategoryNameOption = new Option<string?>("--new-category-name") { Description = "Create a resource category and file it there" };
        var command = new Command("upload-image", "Upload an image into a campaign and link it")
        {
            idOption, fileOption, categoryIdOption, newCategoryNameOption
        };
        command.Validators.Add(result =>
        {
            if (result.GetValue(categoryIdOption) is not null && result.GetValue(newCategoryNameOption) is not null)
                result.AddError("Pass --category-id or --new-category-name, not both.");
        });
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. OwnerOnly, "",
            "A real PNG, JPEG, WebP or GIF, max 5 MB, linked at gm visibility. An admin",
            "may set a lower per-file limit (413) or disable uploads (403); admins are",
            "exempt. The resource_id it returns is the file id [[image:<file-id>]] takes."]);
        command.AddExamples("grimoire-cli campaigns files upload-image --id <campaign-id> --file map.png");
        command.AddResponseExample<Generated.Models.LinkedResourceOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            string result;
            try
            {
                result = await new CampaignFilesService(client).UploadImageAsync(
                    parseResult.GetValue(idOption)!,
                    parseResult.GetValue(fileOption)!,
                    parseResult.GetValue(categoryIdOption),
                    parseResult.GetValue(newCategoryNameOption));
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
}
