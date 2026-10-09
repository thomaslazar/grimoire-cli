using System.CommandLine;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

/// <summary>
/// The filter and sort flags of maps, tokens, audio and models list. Shared
/// because the server declares them once for all four, in
/// <c>routers/_browse.py</c> (<c>media_browse_params</c>).
/// </summary>
internal sealed class MediaBrowseOptions
{
    private static readonly string[] Sorts = ["path", "name", "size", "added_at", "title", "duration"];
    private static readonly string[] Orders = ["asc", "desc"];

    private readonly Option<string?> _query = new("--query") { Description = "Search text; see Query syntax" };
    private readonly Option<string?> _tags = new("--tags") { Description = "Tag filter as JSON; see Tag filter" };
    private readonly Option<bool> _favorites = new("--favorites") { Description = "Only the caller's favorites" };
    private readonly Option<string?> _addedSince = new("--added-since") { Description = "Only items added at or after this ISO 8601 time" };
    private readonly Option<string?> _folder = new("--folder") { Description = "Filter by exact folder path" };
    private readonly Option<string?> _sort = OptionHelpers.Choice("--sort", "Row order; default path", Sorts);
    private readonly Option<string?> _order = OptionHelpers.Choice("--order", "Sort direction; default asc", Orders);

    /// <summary>Adds the flags and the help that teaches them.</summary>
    public void AddTo(Command command, string collection)
    {
        command.Options.Add(_query);
        command.Options.Add(_tags);
        command.Options.Add(_favorites);
        command.Options.Add(_addedSince);
        command.Options.Add(_folder);
        command.Options.Add(_sort);
        command.Options.Add(_order);
        command.AddHelpSection("Filters", HelpSectionPosition.Top,
            $"--folder is one exact folder, not a subtree: folder_path from {collection} get,",
            "relative to the collection root; \"\" is the root.",
            "",
            "--sort path is folder then name, so a page is a contiguous run of",
            "folders; name sorts across the whole tree. title and duration fall back",
            "to name outside audio. Undated items sort last under added_at either",
            "way, and never match --added-since. A time without an offset is UTC.");
        command.AddHelpSection("Query syntax", HelpSectionPosition.Top,
            "Bare text matches the name, the folder path, or a tag (own or a",
            "parent folder's). Fields, aliases in parens: title (name) and",
            "filename (file) match the name; tag (tags); artist (artists) and",
            "album, audio only. Any other search field matches nothing.",
            "",
            "Different fields narrow, repeating one widens. Quote a multi-word",
            "value: tag:\"dark forest\".");
        command.AddHelpSection("Tag filter", HelpSectionPosition.Top,
            "A JSON list. Flat strings must all match: [\"forest\",\"night\"].",
            "Groups OR their tags and must all hold; exclude means none of:",
            "[{\"mode\":\"include\",\"tags\":[\"forest\",\"swamp\"]},",
            " {\"mode\":\"exclude\",\"tags\":[\"night\"]}]",
            "__grim:none__ / __grim:any__ match items with no tag / any tag. A",
            "folder's tags count for everything beneath it. Malformed JSON is a 400.");
    }

    public MediaBrowseFilter Read(ParseResult parseResult) => new(
        parseResult.GetValue(_query),
        parseResult.GetValue(_tags),
        parseResult.GetValue(_favorites) ? true : null,
        parseResult.GetValue(_addedSince),
        parseResult.GetValue(_folder),
        parseResult.GetValue(_sort),
        parseResult.GetValue(_order));
}
