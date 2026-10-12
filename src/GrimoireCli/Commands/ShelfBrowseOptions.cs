using System.CommandLine;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

/// <summary>
/// The filter and sort flags of systems books list and folder-counts. Shared
/// because the server declares them once for both routes, in
/// <c>routers/systems/books.py</c> (<c>book_browse_params</c>).
/// </summary>
internal sealed class ShelfBrowseOptions
{
    private static readonly string[] Sorts = ["title", "year", "page_count", "size", "product_code", "added_at"];
    private static readonly string[] Orders = ["asc", "desc"];

    private readonly Option<string?> _query = new("--query") { Description = "Search text; see Query syntax" };
    private readonly Option<string?> _tags = new("--tags") { Description = "Tag filter as JSON; see Filters" };
    private readonly Option<bool> _favorites = new("--favorites") { Description = "Only the caller's favorites" };
    private readonly Option<string?> _addedSince = new("--added-since") { Description = "Only books added at or after this ISO 8601 time" };
    private readonly Option<bool?> _explicit = new("--explicit") { Description = "Keep only books with this explicit flag (true | false)" };
    private readonly Option<string?> _genre = new("--genre") { Description = "Only books with this genre" };
    private readonly Option<string?> _productCode = new("--product-code") { Description = "Only books whose product code starts with this" };
    private readonly Option<string?> _category = new("--category") { Description = "Only books in this category (core, supplement, adventure, …)" };
    private readonly Option<string?> _folder = new("--folder") { Description = "Only books directly in this subfolder of --category" };
    private readonly Option<string?> _sort = OptionHelpers.Choice("--sort", "Row order; default title", Sorts);
    private readonly Option<string?> _order = OptionHelpers.Choice("--order", "Sort direction; default asc", Orders);

    /// <summary>Adds the flags and the help that teaches them.</summary>
    public void AddTo(Command command)
    {
        command.Options.Add(_query);
        command.Options.Add(_tags);
        command.Options.Add(_favorites);
        command.Options.Add(_addedSince);
        command.Options.Add(_explicit);
        command.Options.Add(_genre);
        command.Options.Add(_productCode);
        command.Options.Add(_category);
        command.Options.Add(_folder);
        command.Options.Add(_sort);
        command.Options.Add(_order);
        command.AddHelpSection("Filters", HelpSectionPosition.Top,
            "--tags matches the book's own tags only, not folder tags. A JSON list:",
            "flat strings must all match, [\"core\",\"pdf\"]; groups OR their tags",
            "and must all hold, exclude meaning none of:",
            "[{\"mode\":\"include\",\"tags\":[\"a\",\"b\"]},{\"mode\":\"exclude\",\"tags\":[\"c\"]}]",
            "",
            "--genre (exact, case-insensitive) and --product-code (a prefix) also",
            "take __grim:none__ / __grim:any__ for books with none / any.",
            "",
            "--category is the normalised value and case-sensitive. --folder is the",
            "subfolder below the category directory, \"\" for the category's own; it",
            "needs --category.",
            "",
            "Undated books never match --added-since; a time without an offset is",
            "UTC. Books with no year or date sort last either way.");
        command.AddHelpSection("Query syntax", HelpSectionPosition.Top,
            "Bare text matches the title, filename or product code. Fields, aliases",
            "in parens: title (name), filename (file), author (authors), artist",
            "(artists), publisher, system (game), category, tag (tags), year, isbn,",
            "code (sku, product_code), language (lang), description (desc).",
            "",
            "Different fields narrow, repeating one widens. Quote a multi-word",
            "value: author:\"Ulrich Kiesow\".");
    }

    public ShelfBrowseFilter Read(ParseResult parseResult) => new(
        parseResult.GetValue(_query),
        parseResult.GetValue(_tags),
        parseResult.GetValue(_favorites) ? true : null,
        parseResult.GetValue(_addedSince),
        parseResult.GetValue(_explicit),
        parseResult.GetValue(_genre),
        parseResult.GetValue(_productCode),
        parseResult.GetValue(_category),
        parseResult.GetValue(_folder),
        parseResult.GetValue(_sort),
        parseResult.GetValue(_order));
}
