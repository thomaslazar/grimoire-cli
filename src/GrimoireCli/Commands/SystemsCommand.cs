using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class SystemsCommand
{
    private static readonly string[] SystemSortKeys = ["name", "book_count", "page_count", "year"];
    private static readonly string[] BookSortKeys = ["category", "title", "page_count", "year"];

    public static Command Create()
    {
        var command = new Command("systems", "Game systems (the folders under books/)");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        return command;
    }

    /// <summary>
    /// An option restricted to a fixed value set. The server silently falls back to
    /// its default sort when given an unknown key, so an unrecognised value is
    /// rejected here instead of returning differently-ordered data with exit 0.
    /// </summary>
    private static Option<string?> ChoiceOption(string name, string description, string[] allowed)
    {
        var option = new Option<string?>(name) { Description = description };
        option.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (value is not null && !allowed.Contains(value))
                result.AddError($"'{value}' is not a valid value for {name}. Must be one of: {string.Join(", ", allowed)}");
        });
        option.CompletionSources.Add(allowed);
        return option;
    }

    private static Command CreateListCommand()
    {
        var sortOption = ChoiceOption("--sort", "Sort field (name | book_count | page_count | year); default name", SystemSortKeys);
        var descOption = new Option<bool>("--desc") { Description = "Sort descending" };
        var genreOption = new Option<string?>("--genre") { Description = "Filter by genre" };
        var familyOption = new Option<string?>("--family") { Description = "Filter by system family" };
        var parentOption = new Option<string?>("--parent-system") { Description = "Filter by parent system" };
        var editionOption = new Option<string?>("--edition") { Description = "Filter by edition" };
        var licenseOption = new Option<string?>("--license") { Description = "Filter by license" };
        var explicitOption = new Option<bool?>("--explicit") { Description = "Filter by explicit flag (true | false); omit for both" };
        var command = new Command("list", "List all game systems")
        {
            sortOption, descOption, genreOption, familyOption,
            parentOption, editionOption, licenseOption, explicitOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Filters are case-insensitive exact matches, not substrings: --edition 5",
            "does not match 5e. They test stored metadata, which the scanner leaves",
            "empty — a freshly imported system matches no filter at all.");
        command.AddExamples(
            "grimoire-cli systems list",
            "grimoire-cli systems list --sort book_count --desc",
            "grimoire-cli systems list --family Shadowrun --edition 6",
            "grimoire-cli systems list --explicit false");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemsService(client);
            var result = await service.ListAsync(
                parseResult.GetValue(sortOption),
                parseResult.GetValue(descOption),
                parseResult.GetValue(genreOption),
                parseResult.GetValue(familyOption),
                parseResult.GetValue(parentOption),
                parseResult.GetValue(editionOption),
                parseResult.GetValue(licenseOption),
                parseResult.GetValue(explicitOption));
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.ListGameSystemSummary);
            return 0;
        });
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var bookSortOption = ChoiceOption("--book-sort", "Sort the books (category | title | page_count | year); default category", BookSortKeys);
        var bookDescOption = new Option<bool>("--book-desc") { Description = "Sort the books descending" };
        var genreOption = new Option<string?>("--genre") { Description = "Keep only books with this genre" };
        var categoryOption = new Option<string?>("--category") { Description = "Keep only books in this category (core | supplement | adventure | character-sheet | map | handout | homebrew | starter-set)" };
        var explicitOption = new Option<bool?>("--explicit") { Description = "Keep only books with this explicit flag (true | false)" };
        var command = new Command("get", "Get one game system, with its books")
        {
            idOption, bookSortOption, bookDescOption, genreOption, categoryOption, explicitOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--genre, --category and --explicit filter the books, not the system, and",
            "book_count / total_page_count are recomputed from the filtered list — so",
            "--category core reports counts for the core books alone.",
            "",
            "--category takes the normalised category, not the folder name:",
            "'supplement', not 'supplements'. It is also case-sensitive — 'Core'",
            "matches nothing — while --genre is case-insensitive.");
        command.AddExamples(
            "grimoire-cli systems get --id <system-id>",
            "grimoire-cli systems get --id <system-id> --category core",
            "grimoire-cli systems get --id <system-id> --book-sort page_count --book-desc");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemsService(client);
            var result = await service.GetAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(bookSortOption),
                parseResult.GetValue(bookDescOption),
                parseResult.GetValue(genreOption),
                parseResult.GetValue(categoryOption),
                parseResult.GetValue(explicitOption));
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.GameSystemDetail);
            return 0;
        });
        return command;
    }
}
