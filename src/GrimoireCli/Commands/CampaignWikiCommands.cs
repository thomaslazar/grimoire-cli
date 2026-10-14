using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignWikiCommands
{
    public static Command Create()
    {
        var command = new Command("wiki", "A campaign's wiki pages");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateSearchCommand());
        command.Subcommands.Add(CreateTitlesCommand());
        return command;
    }

    private static Option<string> CampaignIdOption() =>
        new("--id") { Description = "Campaign ID", Required = true };

    private static Option<string> PageIdOption() =>
        new("--page-id") { Description = "Wiki page ID", Required = true };

    private static Command CreateListCommand()
    {
        var idOption = CampaignIdOption();
        var mineOption = new Option<bool>("--mine") { Description = "Only pages you authored" };
        var includeHiddenOption = new Option<bool>("--include-hidden") { Description = "Include pages you hid" };
        var command = new Command("list", "List the wiki pages you can see")
        {
            idOption, mineOption, includeHiddenOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Ordered by the manual order, then title. Each row says whether you can",
            "edit or delete it; nesting is by parent_id.");
        command.AddExamples(
            "grimoire-cli campaigns wiki list --id <campaign-id>",
            "grimoire-cli campaigns wiki list --id <campaign-id> --mine");
        command.AddResponseExampleArray<Generated.Models.WikiPageListItem>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).ListAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(mineOption),
                parseResult.GetValue(includeHiddenOption)));
            return 0;
        });
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = CampaignIdOption();
        var pageIdOption = PageIdOption();
        var command = new Command("get", "Get one wiki page with its body and backlinks")
        {
            idOption, pageIdOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Anyone but the campaign owner gets the body with GM secrets removed,",
            "with no marker where they were.");
        command.AddExamples("grimoire-cli campaigns wiki get --id <campaign-id> --page-id <page-id>");
        command.AddResponseExample<Generated.Models.WikiPageDetailOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).GetAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(pageIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateSearchCommand()
    {
        var idOption = CampaignIdOption();
        var queryOption = new Option<string>("--query") { Description = "Text to find, 1-200 characters", Required = true };
        queryOption.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>() ?? "";
            if (value.Length is < 1 or > 200)
                result.AddError("--query must be 1-200 characters.");
        });
        var command = new Command("search", "Search wiki page titles and bodies")
        {
            idOption, queryOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "A plain substring, case-insensitive, over title and body — no query",
            "syntax. Hidden pages are left out; non-owners never match secret text.");
        command.AddExamples("grimoire-cli campaigns wiki search --id <campaign-id> --query Thornwall");
        command.AddResponseExample<Generated.Models.WikiSearchOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).SearchAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(queryOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateTitlesCommand()
    {
        var idOption = CampaignIdOption();
        var command = new Command("titles", "Page ids, titles and headings for writing links")
        {
            idOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "ambiguous is true when another visible page shares the title; link",
            "such a page as [[Title:id-<page-id>]]. Headings feed [[Title:#Heading]].");
        command.AddExamples("grimoire-cli campaigns wiki titles --id <campaign-id>");
        command.AddResponseExampleArray<Generated.Models.WikiPageTitleOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).TitlesAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }
}
