using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignCategoriesCommands
{
    private static readonly string[] OwnerOnly =
    [
        "Owner only, no admin override. 403 also when the owner's campaign",
        "access is disabled or the campaign is archived.",
    ];
    private static readonly string[] DeleteModes = ["uncategorize", "delete_items"];

    public static Command Create()
    {
        var command = new Command("categories", "A campaign's resource categories");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateReorderCommand());
        command.Subcommands.Add(CreateGroupOrderCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var command = new Command("list", "List a campaign's resource categories") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, "Owner or accepted member.");
        command.AddExamples("grimoire-cli campaigns categories list --id <campaign-id>");
        command.AddResponseExampleArray<Generated.Models.CampaignCategoryOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignCategoriesService(client).ListAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateCreateCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var nameOption = new Option<string>("--name") { Description = "Category name", Required = true };
        var iconOption = new Option<string?>("--icon") { Description = "Lucide icon key or an emoji" };
        var iconColorOption = new Option<string?>("--icon-color") { Description = "Preset colour token or #rrggbb" };
        var command = new Command("create", "Create a resource category") { idOption, nameOption, iconOption, iconColorOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, OwnerOnly);
        command.AddResponseExample<Generated.Models.CampaignCategoryOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new CampaignCategoriesService(client).CreateAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(nameOption)!,
                parseResult.GetValue(iconOption),
                parseResult.GetValue(iconColorOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var categoryIdOption = new Option<string>("--category-id") { Description = "Category ID", Required = true };
        var nameOption = new Option<string?>("--name") { Description = "Category name" };
        var iconOption = new Option<string?>("--icon") { Description = "Lucide icon key or an emoji; \"\" clears it" };
        var iconColorOption = new Option<string?>("--icon-color") { Description = "Preset colour token or #rrggbb; \"\" clears it" };
        var command = new Command("update", "Rename or restyle a resource category")
        {
            idOption, categoryIdOption, nameOption, iconOption, iconColorOption
        };
        command.Validators.Add(result =>
        {
            var hasName = result.GetValue(nameOption) is not null;
            var hasIcon = result.GetValue(iconOption) is not null;
            var hasIconColor = result.GetValue(iconColorOption) is not null;
            if (!hasName && !hasIcon && !hasIconColor)
                result.AddError("Provide --name, --icon or --icon-color.");
        });
        command.AddHelpSection("Notes", HelpSectionPosition.Top, OwnerOnly);
        command.AddResponseExample<Generated.Models.CampaignCategoryOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new CampaignCategoriesService(client).UpdateAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(categoryIdOption)!,
                parseResult.GetValue(nameOption),
                parseResult.GetValue(iconOption),
                parseResult.GetValue(iconColorOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var categoryIdOption = new Option<string>("--category-id") { Description = "Category ID", Required = true };
        var modeOption = OptionHelpers.Choice("--mode", "What happens to its links; default uncategorize", DeleteModes);
        var command = new Command("delete", "Delete a resource category") { idOption, categoryIdOption, modeOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. OwnerOnly, "",
            "uncategorize moves its links back to their type groups. delete_items",
            "unlinks them, and orphans any uploaded files among them on disk.",
            "",
            "An unknown category id also answers 204: stdout carries no body."]);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new CampaignCategoriesService(client).DeleteAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(categoryIdOption)!, parseResult.GetValue(modeOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateReorderCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var orderedIdsOption = new Option<string[]>("--ordered-ids")
        {
            Description = "Category IDs in the new order",
            AllowMultipleArgumentsPerToken = true,
            Required = true,
        };
        var command = new Command("reorder", "Set the order of a campaign's resource categories") { idOption, orderedIdsOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. OwnerOnly, "",
            "Pass every category id: unknown ones are skipped and unlisted ones keep",
            "their old position."]);
        command.AddExamples("grimoire-cli campaigns categories reorder --id <campaign-id> --ordered-ids <category-id> <category-id>");
        command.AddResponseExample<Generated.Models.Backend__routers__campaigns___response_schemas__OkResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new CampaignCategoriesService(client).ReorderAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(orderedIdsOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateGroupOrderCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var orderedKeysOption = new Option<string[]>("--ordered-keys")
        {
            Description = "Group keys in the new order",
            AllowMultipleArgumentsPerToken = true,
            Required = true,
        };
        var command = new Command("group-order", "Set the order of the resource panel's groups") { idOption, orderedKeysOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. OwnerOnly, "",
            "Keys are type:book, type:map, type:token, type:audio, type:file and",
            "cat:<category-id>. Anything else is dropped without an error,",
            "type:model included; the response lists the keys kept."]);
        command.AddExamples("grimoire-cli campaigns categories group-order --id <campaign-id> --ordered-keys cat:<category-id> type:book type:map");
        command.AddResponseExample<Generated.Models.ResourceGroupOrderOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new CampaignCategoriesService(client).GroupOrderAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(orderedKeysOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
