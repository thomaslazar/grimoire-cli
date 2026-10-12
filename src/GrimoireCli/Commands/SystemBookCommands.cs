using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class SystemBookCommands
{
    public static Command Create()
    {
        var command = new Command("books", "A system's books, a page at a time");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateFolderCountsCommand());
        command.Subcommands.Add(CreateFacetsCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var limitOption = OptionHelpers.Range("--limit", "Results per page", 1, 500);
        limitOption.DefaultValueFactory = _ => 100;
        var offsetOption = OptionHelpers.Range("--offset", "Items to skip", 0);
        var shelf = new ShelfBrowseOptions();
        var command = new Command("list", "A page of a system's books, with their metadata")
        {
            idOption, limitOption, offsetOption
        };
        shelf.AddTo(command);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Page with --offset against total in the response.",
            "",
            "Variants are hidden; the account's explicit permission and access",
            "settings filter the shelf server-side.");
        command.AddExamples(
            "grimoire-cli systems books list --id <system-id>",
            "grimoire-cli systems books list --id <system-id> --category core --folder errata",
            "grimoire-cli systems books list --id <system-id> --genre __grim:none__ --limit 500");
        command.AddResponseExample<Generated.Models.SystemBooksPage>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemBooksService(client);
            var result = await service.ListAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(limitOption),
                parseResult.GetValue(offsetOption),
                shelf.Read(parseResult));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateFolderCountsCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var shelf = new ShelfBrowseOptions();
        var command = new Command("folder-counts", "Directories holding a system's matching books, with counts")
        {
            idOption
        };
        shelf.AddTo(command);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "One row per directory: count covers the books directly in it,",
            "not its subfolders. path is the subfolder below the category directory",
            "(\"\" for the category's own); dir is library-relative. Two directories",
            "can share a category and path.",
            "",
            "first is the active --sort's value for the folder's first book; null",
            "under title.",
            "",
            "The account's explicit permission and access settings filter the counts.");
        command.AddExamples(
            "grimoire-cli systems books folder-counts --id <system-id>",
            "grimoire-cli systems books folder-counts --id <system-id> --genre __grim:none__");
        command.AddResponseExample<Generated.Models.ShelfGroupsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemBooksService(client);
            var result = await service.FolderCountsAsync(
                parseResult.GetValue(idOption)!,
                shelf.Read(parseResult));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateFacetsCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var command = new Command("facets", "The categories, genres, code prefixes and tags a system's books use")
        {
            idOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Values come from every book on the system the account can see,",
            "before any filter. product_code_prefixes are each code's leading",
            "letters, upper-cased.");
        command.AddExamples("grimoire-cli systems books facets --id <system-id>");
        command.AddResponseExample<Generated.Models.ShelfFacetsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemBooksService(client);
            var result = await service.FacetsAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
