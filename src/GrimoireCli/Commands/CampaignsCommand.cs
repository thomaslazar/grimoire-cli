using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignsCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly string[] OwnerOnly =
    [
        "Owner only, no admin override. 403 also when the owner's campaign",
        "access is disabled or the campaign is archived.",
    ];

    public static Command Create()
    {
        var command = new Command("campaigns", "Campaigns and the library items linked into them");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CampaignResourcesCommands.Create());
        command.Subcommands.Add(CampaignCategoriesCommands.Create());
        command.Subcommands.Add(CampaignFilesCommands.Create());
        return command;
    }

    private static Command CreateListCommand()
    {
        var archivedOption = new Option<bool>("--include-archived") { Description = "Also list archived campaigns" };
        var command = new Command("list", "List campaigns you own or have joined") { archivedOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Archived campaigns are hidden unless --include-archived; they are",
            "read-only, even for the owner.");
        command.AddExamples("grimoire-cli campaigns list", "grimoire-cli campaigns list --include-archived");
        command.AddResponseExampleArray<Generated.Models.CampaignOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignsService(client).ListAsync(parseResult.GetValue(archivedOption)));
            return 0;
        });
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var command = new Command("get", "Get one campaign") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, "Owner or accepted member only.");
        command.AddExamples("grimoire-cli campaigns get --id <campaign-id>");
        command.AddResponseExample<Generated.Models.CampaignOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignsService(client).GetAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateCreateCommand()
    {
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("create", "Create a campaign you own") { inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Only name is required. The caller becomes the owner, and only the",
            "owner can write to it afterwards.",
            "",
            "is_gm_campaign true needs the gm or admin role; guests cannot create.",
            "",
            "resources links items at creation, as resources bulk does.");
        command.AddExamples(
            "grimoire-cli campaigns create --input campaign.json",
            "echo '{\"name\":\"Curse of Strahd\"}' | grimoire-cli campaigns create --stdin");
        command.AddRequestShape<Generated.Models.CampaignCreate>();
        command.AddResponseExample<Generated.Models.CampaignOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.CampaignCreate.CreateFromDiscriminatorValue, "it is assigned by the server");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignsService(client).CreateAsync(body));
            return 0;
        });
        return command;
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("update", "Update a campaign's details") { idOption, inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. OwnerOnly, "", "system_name \"\" clears it."]);
        command.AddExamples("echo '{\"description\":\"Session zero\"}' | grimoire-cli campaigns update --id <id> --stdin");
        command.AddRequestShape<Generated.Models.CampaignUpdate>();
        command.AddResponseExample<Generated.Models.CampaignOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.CampaignUpdate.CreateFromDiscriminatorValue, "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignsService(client).UpdateAsync(parseResult.GetValue(idOption)!, body));
            return 0;
        });
        return command;
    }
}
