using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class SystemsCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly string[] SystemSortKeys = ["name", "book_count", "page_count", "year"];
    private static readonly string[] BookSortKeys = ["category", "title", "page_count", "year"];

    public static Command Create()
    {
        var command = new Command("systems", "Game systems (the folders under books/)");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateBatchUpdateCommand());
        command.Subcommands.Add(CreateBatchTagCommand());
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
        var sortOption = ChoiceOption("--sort", "Sort field; default name", SystemSortKeys);
        var descOption = new Option<bool>("--desc") { Description = "Sort descending" };
        var genreOption = new Option<string?>("--genre") { Description = "Filter by genre" };
        var familyOption = new Option<string?>("--family") { Description = "Filter by system family" };
        var parentOption = new Option<string?>("--parent-system") { Description = "Filter by parent system" };
        var editionOption = new Option<string?>("--edition") { Description = "Filter by edition" };
        var licenseOption = new Option<string?>("--license") { Description = "Filter by license" };
        var explicitOption = new Option<bool?>("--explicit") { Description = "Filter by explicit flag (true | false); omit for both" };
        var parentIdOption = new Option<string?>("--parent-id") { Description = "List only the children of this container" };
        var includeChildrenOption = new Option<bool>("--include-children") { Description = "Include container children (hidden by default)" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("list", "List all game systems")
        {
            sortOption, descOption, genreOption, familyOption,
            parentOption, editionOption, licenseOption, explicitOption,
            parentIdOption, includeChildrenOption,
            serverOption, tokenOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Filters are case-insensitive exact matches, not substrings: --edition 5",
            "does not match 5e. A freshly imported system has no metadata and",
            "matches nothing.",
            "",
            "Children are hidden before filters apply, so --genre/--family/--edition/",
            "--license return [] unless --include-children is passed; --parent-id",
            "implies it.");
        command.AddExamples(
            "grimoire-cli systems list",
            "grimoire-cli systems list --sort book_count --desc",
            "grimoire-cli systems list --include-children --family Shadowrun",
            "grimoire-cli systems list --parent-id <container-id>",
            "grimoire-cli systems list --explicit false");
        command.AddResponseExampleArray<GameSystemSummary>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var server = parseResult.GetValue(serverOption);
            var token = parseResult.GetValue(tokenOption);
            var (client, _) = CommandHelper.BuildClient(serverOverride: server, tokenOverride: token);
            var service = new SystemsService(client);
            var result = await service.ListAsync(
                parseResult.GetValue(sortOption),
                parseResult.GetValue(descOption),
                parseResult.GetValue(genreOption),
                parseResult.GetValue(familyOption),
                parseResult.GetValue(parentOption),
                parseResult.GetValue(editionOption),
                parseResult.GetValue(licenseOption),
                parseResult.GetValue(explicitOption),
                parseResult.GetValue(parentIdOption),
                parseResult.GetValue(includeChildrenOption));
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.ListGameSystemSummary);
            return 0;
        });
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var bookSortOption = ChoiceOption("--book-sort", "Sort the books; default category", BookSortKeys);
        var bookDescOption = new Option<bool>("--book-desc") { Description = "Sort the books descending" };
        var genreOption = new Option<string?>("--genre") { Description = "Keep only books with this genre" };
        var categoryOption = new Option<string?>("--category") { Description = "Keep only books in this category (core, supplement, adventure, …)" };
        var explicitOption = new Option<bool?>("--explicit") { Description = "Keep only books with this explicit flag (true | false)" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("get", "Get one game system, with its books")
        {
            idOption, bookSortOption, bookDescOption, genreOption, categoryOption, explicitOption,
            serverOption, tokenOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--genre, --category and --explicit filter the books, not the system;",
            "book_count and total_page_count are recomputed from what survives.",
            "",
            "--category takes the normalised value, not the folder name",
            "('supplement', not 'supplements'), and is case-sensitive; --genre is",
            "not. Values are open-ended: an unmapped folder becomes its own slug,",
            "and a book with no subfolder under a system-agnostic root is",
            "'uncategorized'.",
            "",
            "--book-desc applies only to --book-sort title|page_count|year.");
        command.AddExamples(
            "grimoire-cli systems get --id <system-id>",
            "grimoire-cli systems get --id <system-id> --category core",
            "grimoire-cli systems get --id <system-id> --book-sort page_count --book-desc");
        command.AddResponseExample<GameSystemDetail>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var server = parseResult.GetValue(serverOption);
            var token = parseResult.GetValue(tokenOption);
            var (client, _) = CommandHelper.BuildClient(serverOverride: server, tokenOverride: token);
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

    /// <summary>
    /// Declares --input / --stdin as mutually exclusive and exactly one required,
    /// as a command validator so the refusal is a parse error (exit 1) before any
    /// client is built.
    /// </summary>
    private static void RequireExactlyOneBodySource(
        Command command, Option<string?> inputOption, Option<bool> stdinOption)
    {
        command.Validators.Add(result =>
        {
            var hasInput = result.GetValue(inputOption) != null;
            var hasStdin = result.GetValue(stdinOption);
            if (hasInput && hasStdin)
                result.AddError(JsonBodyInput.BothSourcesMessage);
            else if (!hasInput && !hasStdin)
                result.AddError(JsonBodyInput.NeitherSourceMessage);
        });
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("update", "Update one game system's metadata")
        {
            idOption, inputOption, stdinOption, serverOption, tokenOption
        };
        command.AddRoleRequired("gm or admin");
        RequireExactlyOneBodySource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Body is Grimoire's own field object, without id. Editable fields:",
            "name, description, publishers, character_builder_url,",
            "character_builder_urls, urls, tags, genre, genres, dice_materials,",
            "system_family, parent_system, edition, license, year, cover_book_id,",
            "is_explicit. An unknown field is rejected before the request is made.",
            "",
            "Renaming is permanent: setting name marks it custom, and the scanner",
            "then never re-derives it from the folder again, on any later rescan.",
            "",
            "Clear a field with \"\". An explicit null is dropped server-side and",
            "does nothing.",
            "",
            "genre and character_builder_url are legacy singles; prefer genres",
            "and character_builder_urls.",
            "",
            "Responds {\"status\": \"ok\"} — it does not echo the system, so read",
            "the result back with: grimoire-cli systems get --id <id>");
        command.AddExamples(
            "grimoire-cli systems update --id <id> --input metadata.json",
            "echo '{\"system_family\":\"Shadowrun\"}' | grimoire-cli systems update --id <id> --stdin");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, AppJsonContext.Default.GameSystemUpdateRequest,
                    "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new SystemsService(client);
            var response = await service.UpdateAsync(parseResult.GetValue(idOption)!, body);
            ConsoleOutput.WriteRawJson(response);
            return 0;
        });
        return command;
    }

    private static Command CreateBatchUpdateCommand()
    {
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("batch-update", "Update many game systems in one transaction")
        {
            inputOption, stdinOption, serverOption, tokenOption
        };
        command.AddRoleRequired("gm or admin");
        RequireExactlyOneBodySource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Body is {\"items\": [{\"id\": \"…\", …fields}]}, at most 1000 items;",
            "fields are those of: grimoire-cli systems update --help",
            "",
            "Skip-and-continue: an unresolved id or a rejected item lands in",
            "errors and the rest still apply. Exit 3 means HTTP 200 with a",
            "non-empty errors list — a partial application, not a failure.",
            "",
            "updated reports ids, not fields: an id there means the row resolved,",
            "not that any value changed.");
        command.AddExamples(
            "grimoire-cli systems batch-update --input items.json",
            "jq -c '{items: .}' edits.json | grimoire-cli systems batch-update --stdin");
        command.AddResponseExample<BulkUpdateResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, AppJsonContext.Default.GameSystemBulkUpdateRequest,
                    "put it in each item");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var result = await new SystemsService(client).BatchUpdateAsync(body);
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.BulkUpdateResult);
            return BulkExit.CodeFor(result.Errors);
        });
        return command;
    }

    private static Command CreateBatchTagCommand()
    {
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("batch-tag", "Add tags to many game systems")
        {
            inputOption, stdinOption, serverOption, tokenOption
        };
        command.AddRoleRequired("gm or admin");
        RequireExactlyOneBodySource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Body is {\"ids\": [\"…\"], \"tags\": [\"…\"]}, both non-empty, at most",
            "1000 ids.",
            "",
            "Additive only: it merges with each system's existing tags and never",
            "removes one. To replace a tag set, use batch-update with tags.",
            "",
            "Exit 3 means HTTP 200 with a non-empty errors list — some ids did",
            "not resolve while the rest were tagged.");
        command.AddExamples(
            "grimoire-cli systems batch-tag --input tags.json",
            "echo '{\"ids\":[\"<id>\"],\"tags\":[\"cyberpunk\"]}' | grimoire-cli systems batch-tag --stdin");
        command.AddResponseExample<BulkTagResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, AppJsonContext.Default.BulkAddTagsRequest,
                    "put it in ids");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var result = await new SystemsService(client).BatchTagAsync(body);
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.BulkTagResult);
            return BulkExit.CodeFor(result.Errors);
        });
        return command;
    }
}
