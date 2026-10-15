using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignWikiCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("wiki", "A campaign's wiki pages");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateSearchCommand());
        command.Subcommands.Add(CreateTitlesCommand());
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateReorderCommand());
        command.Subcommands.Add(CreateHideCommand());
        command.Subcommands.Add(CreateUnhideCommand());
        command.Subcommands.Add(CreateExportCommand());
        command.Subcommands.Add(CreateImportCommand());
        command.Subcommands.Add(CampaignWikiTemplateCommands.Create());
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
            "Ordered by the manual order, then title; nesting is by parent_id.");
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

    private static readonly string[] Archived =
    [
        "Refused while the campaign is archived.",
    ];

    private static Command CreateCreateCommand()
    {
        var idOption = CampaignIdOption();
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("create", "Create a wiki page") { idOption, inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        WikiPageHelp.AddTo(command);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. Archived, "",
            "Needs campaign membership. An empty title becomes Untitled; parent_id",
            "must be a page you can edit."]);
        command.AddExamples(
            "echo '{\"title\":\"Baron Valdric\",\"body\":\"Rules [[Thornwall]].\",\"visibility\":\"group\"}' | grimoire-cli campaigns wiki create --id <campaign-id> --stdin",
            "jq -n --rawfile body npc.md '{title:\"Baron Valdric\",body:$body}' | grimoire-cli campaigns wiki create --id <campaign-id> --stdin");
        command.AddRequestShape<Generated.Models.WikiPageCreate>();
        command.AddResponseExample<Generated.Models.WikiPageSummaryOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.WikiPageCreate.CreateFromDiscriminatorValue, "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).CreateAsync(parseResult.GetValue(idOption)!, body));
            return 0;
        });
        return command;
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = CampaignIdOption();
        var pageIdOption = PageIdOption();
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("update", "Update a wiki page") { idOption, pageIdOption, inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        WikiPageHelp.AddTo(command);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. Archived, "",
            "Send only the fields to change. parent_id \"\" moves the page to the top",
            "level; \"\" clears icon and icon_color. Needs edit access (see",
            "Visibility)."]);
        command.AddExamples(
            "echo '{\"title\":\"Baron Valdric the Grey\"}' | grimoire-cli campaigns wiki update --id <campaign-id> --page-id <page-id> --stdin");
        command.AddRequestShape<Generated.Models.WikiPageUpdate>();
        command.AddResponseExample<Generated.Models.WikiPageSummaryOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.WikiPageUpdate.CreateFromDiscriminatorValue, "pass it with --page-id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).UpdateAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(pageIdOption)!, body));
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = CampaignIdOption();
        var pageIdOption = PageIdOption();
        var command = new Command("delete", "Delete a wiki page") { idOption, pageIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. Archived, "",
            "Author only. The page's children move up to its parent. An unknown",
            "page id succeeds silently."]);
        command.AddExamples("grimoire-cli campaigns wiki delete --id <campaign-id> --page-id <page-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).DeleteAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(pageIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateReorderCommand()
    {
        var idOption = CampaignIdOption();
        var orderedIdsOption = new Option<string[]>("--ordered-ids")
        {
            Description = "Page IDs in the new order",
            AllowMultipleArgumentsPerToken = true,
            Required = true,
        };
        var command = new Command("reorder", "Set the manual order of wiki pages") { idOption, orderedIdsOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. Archived, "",
            "Re-deals the slots of the listed pages you can edit; unknown pages and",
            "pages you cannot edit are skipped and keep their slots."]);
        command.AddExamples("grimoire-cli campaigns wiki reorder --id <campaign-id> --ordered-ids <page-id> <page-id>");
        command.AddResponseExample<Generated.Models.Backend__routers__campaigns___response_schemas__OkResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).ReorderAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(orderedIdsOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateHideCommand()
    {
        var idOption = CampaignIdOption();
        var pageIdOption = PageIdOption();
        var command = new Command("hide", "Hide a wiki page from your own view") { idOption, pageIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Changes only your view; nobody else's. Hiding a parent hides its",
            "subtree. Refused (409) in a personal campaign.");
        command.AddExamples("grimoire-cli campaigns wiki hide --id <campaign-id> --page-id <page-id>");
        command.AddResponseExample<Generated.Models.WikiPageHiddenOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).HideAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(pageIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateUnhideCommand()
    {
        var idOption = CampaignIdOption();
        var pageIdOption = PageIdOption();
        var command = new Command("unhide", "Bring back a wiki page you hid") { idOption, pageIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "A page hidden because its parent is hidden comes back only when the",
            "parent does.");
        command.AddExamples("grimoire-cli campaigns wiki unhide --id <campaign-id> --page-id <page-id>");
        command.AddResponseExample<Generated.Models.WikiPageHiddenOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).UnhideAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(pageIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateExportCommand()
    {
        var idOption = CampaignIdOption();
        var formatOption = OptionHelpers.Choice("--format", "Export shape; default md", ["md", "mdfile", "json"]);
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for the bytes to stdout",
            Required = true,
        };
        var command = new Command("export", "Export the wiki pages you can see") { idOption, formatOption, outputOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "md: a zip of one <slug>.md per page with metadata frontmatter. json: a",
            "Grimoire bundle. Both re-import with the tree intact. mdfile: one markdown",
            "document for reading; it re-imports as a single page.",
            "",
            "Holds only pages you can view; GM secrets are removed unless you own the",
            "campaign. Works on an archived campaign.",
            "",
            "--output - writes the bytes to stdout; a path writes the file and prints",
            "{path, bytes}.");
        command.AddExamples(
            "grimoire-cli campaigns wiki export --id <campaign-id> --output wiki.zip",
            "grimoire-cli campaigns wiki export --id <campaign-id> --format json --output - > wiki.json");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            await using var stream = await new CampaignWikiService(client).ExportAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(formatOption));
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

    private static Command CreateImportCommand()
    {
        var idOption = CampaignIdOption();
        var fileOption = new Option<string>("--file") { Description = "Local file to import", Required = true };
        var command = new Command("import", "Import pages from a markdown, zip or JSON file") { idOption, fileOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Campaign owner only; an archived campaign answers 409.",
            "",
            "Import never overwrites: every page in the file becomes a new page, so",
            "importing twice duplicates them. Unresolved [[links]] get stubs, as on create.",
            "",
            "Reads .md/.markdown/.txt, Grimoire JSON, or LegendKeeper JSON or .lk, or a",
            ".zip of markdown or JSON files. A zip's folders become nested pages;",
            "Places/Places.md or Places/index.md becomes the Places page itself. To import",
            "a folder, zip it. Max 25 MB.");
        command.AddExamples("grimoire-cli campaigns wiki import --id <campaign-id> --file vault.zip");
        command.AddResponseExample<Generated.Models.WikiImportOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            string result;
            try
            {
                result = await new CampaignWikiService(client).ImportAsync(
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
}
