using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignFilesCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly string[] OwnerOnly =
    [
        "Owner only, no admin override. 403 also when the owner's campaign",
        "access is disabled or the campaign is archived.",
    ];

    public static Command Create()
    {
        var command = new Command("files", "Files uploaded into a campaign");
        command.Subcommands.Add(CreateUploadCommand());
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
}
