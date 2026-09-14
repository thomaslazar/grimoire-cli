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
            "results is page-text hits alone. A title or metadata match lands in",
            "book_matches instead, so a query that found something can still come",
            "back with results empty.",
            "",
            "book_matches, maps, tokens, audio and models are capped at 50 each;",
            "--limit does not raise them.",
            "",
            "field:value filters go inside --query. Any filter switches off the",
            "page-text search; text: switches it back on.",
            "",
            "An unrecognised prefix is searched literally rather than refused, so",
            "a typo returns nothing. The response's fields echoes what was read",
            "as a filter.",
            "A malformed year: value is ignored rather than refused, and fields",
            "still lists it.",
            "",
            "snippet carries literal <mark> HTML.",
            "",
            "--book-id and --system-id drop the map, token, audio and model",
            "results; --book-id also empties book_matches.");
        command.AddHelpSection("Query syntax", HelpSectionPosition.Top,
            "Fields, aliases in parens. Books and media: title (name), filename",
            "(file), tag (tags). Books and audio: artist (artists). Audio only:",
            "album. Books only: author (authors), publisher, system (game),",
            "category, year, isbn, language (lang), description (desc) — any of",
            "these also drops the map, token, audio and model results. text",
            "(content, page) searches page text alone and returns no book_matches.",
            "",
            "Different fields narrow, repeating one widens: system:pbta",
            "category:core matches both, tag:forest tag:swamp matches either.",
            "",
            "Quote a multi-word value — unquoted, only the first word binds to",
            "the field and the rest becomes free text.",
            "",
            "year: takes 1999, >1999, <=2005 or 1999-2005.",
            "Page text also takes fire* as a prefix match — * only at the end.");
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
