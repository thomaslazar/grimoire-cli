using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

/// <summary>
/// Pins on a campaign map page (routers/campaigns/wiki_pins.py,
/// _wiki_maps.py, v1.11.0).
/// </summary>
public static class CampaignWikiPinsCommands
{
    private static readonly string[] Visibilities = ["page", "gm"];

    private static readonly string[] Access =
    [
        "Needs edit rights on the map page, else 403; a page that is not a map",
        "page is a 400. Only the campaign GM sets gm visibility, else 403.",
    ];

    private static readonly string[] Target =
    [
        "--target-title links the page with that title that you can see, else",
        "creates a stub with the map page's visibility, as [[Title]] does.",
        "--target-page-id must be a page you can see, not the map page itself;",
        "else 400.",
    ];

    private static readonly string[] Hidden =
    [
        "A pin you can't see is a 404: a gm pin when you are not the GM, or one",
        "linking a page you can't read. Pin ids are under pins in campaigns wiki get.",
    ];

    public static Command Create()
    {
        var command = new Command("pins", "Pins on a map page");
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        return command;
    }

    private static Option<string> CampaignIdOption() => new("--id") { Description = "Campaign ID", Required = true };

    private static Option<string> PageIdOption() => new("--page-id") { Description = "Map page ID", Required = true };

    private static Option<string> PinIdOption() => new("--pin-id") { Description = "Pin ID", Required = true };

    private static Option<string?> VisibilityOption(string description) =>
        OptionHelpers.Choice("--visibility", description, Visibilities);

    private static Command CreateCreateCommand()
    {
        var idOption = CampaignIdOption();
        var pageIdOption = PageIdOption();
        var xOption = new Option<double>("--x") { Description = "Fraction of the picture's width, 0 to 1", Required = true };
        var yOption = new Option<double>("--y") { Description = "Fraction of the picture's height, 0 to 1", Required = true };
        var labelOption = new Option<string?>("--label") { Description = "Pin label" };
        var targetPageIdOption = new Option<string?>("--target-page-id") { Description = "Page the pin links" };
        var targetTitleOption = new Option<string?>("--target-title") { Description = "Title of the page the pin links" };
        var iconOption = new Option<string?>("--icon") { Description = "Lucide icon key or an emoji" };
        var iconColorOption = new Option<string?>("--icon-color") { Description = "Preset colour token or #rrggbb" };
        var visibilityOption = VisibilityOption("page: whoever sees the map page; gm: the GM alone. Default page");
        var command = new Command("create", "Add a pin to a map page")
        {
            idOption, pageIdOption, xOption, yOption, labelOption, targetPageIdOption, targetTitleOption,
            iconOption, iconColorOption, visibilityOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, [.. Access, "", .. Target, "On create --target-page-id wins over --target-title if both are given."]);
        command.AddExamples("grimoire-cli campaigns wiki pins create --id <campaign-id> --page-id <page-id> --x 0.25 --y 0.5 --label Gate --target-title \"Thornwall Gate\"");
        command.AddResponseExample<Generated.Models.WikiMapPinOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var body = CampaignWikiPinsService.BuildCreateBody(
                parseResult.GetValue(xOption),
                parseResult.GetValue(yOption),
                parseResult.GetValue(labelOption),
                parseResult.GetValue(targetPageIdOption),
                parseResult.GetValue(targetTitleOption),
                parseResult.GetValue(iconOption),
                parseResult.GetValue(iconColorOption),
                parseResult.GetValue(visibilityOption));
            ConsoleOutput.WriteRawJson(await new CampaignWikiPinsService(client).CreateAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(pageIdOption)!, body));
            return 0;
        });
        return command;
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = CampaignIdOption();
        var pageIdOption = PageIdOption();
        var pinIdOption = PinIdOption();
        var xOption = new Option<double?>("--x") { Description = "Fraction of the picture's width, 0 to 1" };
        var yOption = new Option<double?>("--y") { Description = "Fraction of the picture's height, 0 to 1" };
        var labelOption = new Option<string?>("--label") { Description = "Pin label" };
        var targetPageIdOption = new Option<string?>("--target-page-id") { Description = "Page the pin links" };
        var targetTitleOption = new Option<string?>("--target-title") { Description = "Title of the page the pin links" };
        var iconOption = new Option<string?>("--icon") { Description = "Lucide icon key or an emoji; \"\" clears it" };
        var iconColorOption = new Option<string?>("--icon-color") { Description = "Preset colour token or #rrggbb; \"\" clears it" };
        var visibilityOption = VisibilityOption("page: whoever sees the map page; gm: the GM alone");
        var command = new Command("update", "Move or edit a pin on a map page")
        {
            idOption, pageIdOption, pinIdOption, xOption, yOption, labelOption, targetPageIdOption,
            targetTitleOption, iconOption, iconColorOption, visibilityOption
        };
        command.Validators.Add(result =>
        {
            if (result.GetValue(xOption) is null && result.GetValue(yOption) is null &&
                result.GetValue(labelOption) is null && result.GetValue(targetPageIdOption) is null &&
                result.GetValue(targetTitleOption) is null && result.GetValue(iconOption) is null &&
                result.GetValue(iconColorOption) is null && result.GetValue(visibilityOption) is null)
                result.AddError("Provide at least one field to change.");
        });
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. Access, "", .. Target,
            "--target-title wins over --target-page-id; --target-page-id \"\" unlinks.",
            "", .. Hidden]);
        command.AddExamples("grimoire-cli campaigns wiki pins update --id <campaign-id> --page-id <page-id> --pin-id <pin-id> --x 0.75 --visibility gm");
        command.AddResponseExample<Generated.Models.WikiMapPinOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var body = CampaignWikiPinsService.BuildUpdateBody(
                parseResult.GetValue(xOption),
                parseResult.GetValue(yOption),
                parseResult.GetValue(labelOption),
                parseResult.GetValue(targetPageIdOption),
                parseResult.GetValue(targetTitleOption),
                parseResult.GetValue(iconOption),
                parseResult.GetValue(iconColorOption),
                parseResult.GetValue(visibilityOption));
            ConsoleOutput.WriteRawJson(await new CampaignWikiPinsService(client).UpdateAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(pageIdOption)!, parseResult.GetValue(pinIdOption)!, body));
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = CampaignIdOption();
        var pageIdOption = PageIdOption();
        var pinIdOption = PinIdOption();
        var command = new Command("delete", "Remove a pin from a map page") { idOption, pageIdOption, pinIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            ["Needs edit rights on the map page, else 403.", "", .. Hidden]);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiPinsService(client).DeleteAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(pageIdOption)!, parseResult.GetValue(pinIdOption)!));
            return 0;
        });
        return command;
    }
}
