using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class SearchCommand
{
    public static Command Create()
    {
        var queryOption = new Option<string>("--query") { Description = "Search text (2+ characters)", Required = true };
        var limitOption = OptionHelpers.Range("--limit", "Page-text results; default 50, max 200", 1, 200);
        var bookIdOption = new Option<string?>("--book-id") { Description = "Restrict to one book" };
        var systemIdOption = new Option<string?>("--system-id") { Description = "Restrict to one game system" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("search", "Search page text and metadata across the library")
        {
            queryOption, limitOption, bookIdOption, systemIdOption, serverOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "book_matches, maps, tokens and audio are capped at 50 each; --limit",
            "does not raise them.",
            "",
            "field:value filters go inside --query. Any filter switches off the",
            "page-text search; text: switches it back on. search fields lists them.",
            "",
            "An unrecognised prefix is searched literally rather than refused, so",
            "a typo returns nothing. The response's fields echoes what was read",
            "as a filter.",
            "A malformed year: value is ignored rather than refused, and fields",
            "still lists it.",
            "",
            "snippet carries literal <mark> HTML.",
            "",
            "--book-id and --system-id drop the map, token and audio results;",
            "--book-id also empties book_matches.");
        command.AddExamples(
            "grimoire-cli search --query \"dragon\"",
            "grimoire-cli search --query \"author:'Ben Robbins' year:>2010\"",
            "grimoire-cli search --query \"text:fireball\" --system-id <system-id>");
        command.AddResponseExample<Generated.Models.SearchResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new SearchService(client);
            var result = await service.SearchAsync(
                parseResult.GetValue(queryOption)!,
                parseResult.GetValue(limitOption),
                parseResult.GetValue(bookIdOption),
                parseResult.GetValue(systemIdOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        command.Subcommands.Add(CreateFieldsCommand());
        return command;
    }

    private static Command CreateFieldsCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("fields", "The field: prefixes a search query accepts, with their aliases") { serverOption };
        command.AddExamples("grimoire-cli search fields");
        command.AddResponseExample<Generated.Models.SearchFieldsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new SearchService(client);
            var result = await service.FieldsAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
