using System.CommandLine;
using GrimoireCli.Api;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class BooksCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("books", "Read and edit book metadata");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateBatchUpdateCommand());
        command.Subcommands.Add(CreateBatchTagCommand());
        command.Subcommands.Add(CreateReindexCommand());
        command.Subcommands.Add(CreateRescanCommand());
        command.Subcommands.Add(CreateThumbnailCommand());
        foreach (var metadata in MetadataCommands.Create("books"))
            command.Subcommands.Add(metadata);
        return command;
    }

    private static Command CreateListCommand()
    {
        var systemIdOption = new Option<string?>("--system-id") { Description = "Filter by game system" };
        var categoryOption = new Option<string?>("--category") { Description = "Filter by category (core, supplement, adventure, …)" };
        var limitOption = new Option<int>("--limit")
        {
            Description = "Results per page (default 100, max 500)",
            DefaultValueFactory = _ => 100,
        };
        var offsetOption = new Option<int?>("--offset") { Description = "Items to skip" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("list", "List books (defaults to 100 results)")
        {
            systemIdOption, categoryOption, limitOption, offsetOption, serverOption, tokenOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--limit 422s above 500; page with --offset against the total in the",
            "response.",
            "",
            "--category is the normalised value, not the folder name ('supplement',",
            "not 'supplements'), and is case-sensitive: Core matches nothing.",
            "",
            "The account's explicit permission filters the list server-side.");
        command.AddExamples(
            "grimoire-cli books list",
            "grimoire-cli books list --system-id <system-id> --category core",
            "grimoire-cli books list --limit 500 --offset 500");
        command.AddResponseExample<BookListResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var server = parseResult.GetValue(serverOption);
            var token = parseResult.GetValue(tokenOption);
            var (client, _) = CommandHelper.BuildClient(serverOverride: server, tokenOverride: token);
            var service = new BooksService(client);
            var result = await service.ListAsync(
                parseResult.GetValue(systemIdOption),
                parseResult.GetValue(categoryOption),
                parseResult.GetValue(limitOption),
                parseResult.GetValue(offsetOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("get", "Get one book")
        {
            idOption, serverOption, tokenOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "403 if the book is explicit and the account disallows explicit content.");
        command.AddExamples("grimoire-cli books get --id <book-id>");
        command.AddResponseExample<BookDetail>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var server = parseResult.GetValue(serverOption);
            var token = parseResult.GetValue(tokenOption);
            var (client, _) = CommandHelper.BuildClient(serverOverride: server, tokenOverride: token);
            var service = new BooksService(client);
            var result = await service.GetAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("update", "Update one book's metadata")
        {
            idOption, inputOption, stdinOption, serverOption, tokenOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Clear a field with \"\"; an explicit null does nothing.",
            "",
            "year, month and day cannot be cleared at all: null is dropped and \"\"",
            "fails coercion with a 422.",
            "",
            "tags replace the set. To add without removing, use batch-tag.",
            "",
            "Responds {\"status\": \"ok\"} and echoes nothing — read back with:",
            "grimoire-cli books get --id <id>");
        command.AddExamples(
            "grimoire-cli books update --id <id> --input metadata.json",
            "echo '{\"title\":\"New Title\"}' | grimoire-cli books update --id <id> --stdin");
        command.AddRequestShape<Generated.Models.BookUpdate>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.BookUpdate.CreateFromDiscriminatorValue,
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
            var service = new BooksService(client);
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
        var command = new Command("batch-update", "Update many books in one transaction")
        {
            inputOption, stdinOption, serverOption, tokenOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "At most 1000 items. Each item requires id.",
            "",
            "Skip-and-continue: a bad id or item lands in errors, the rest apply.",
            "Exit 3 is HTTP 200 with a non-empty errors list — a partial write.",
            "updated lists the ids that resolved, not the fields that changed.",
            "",
            "\"\" not null clears a field, and year/month/day cannot be cleared — see",
            "books update.");
        command.AddExamples(
            "grimoire-cli books batch-update --input items.json",
            "jq -c '{items: .}' edits.json | grimoire-cli books batch-update --stdin");
        command.AddRequestShape<Generated.Models.BookBulkUpdate>();
        command.AddResponseExample<BulkUpdateResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.BookBulkUpdate.CreateFromDiscriminatorValue,
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
            var result = await new BooksService(client).BatchUpdateAsync(body);
            ConsoleOutput.WriteRawJson(result);
            return BulkExit.CodeFor(GrimoireApiClient.HasItems(result, "errors"));
        });
        return command;
    }

    private static Command CreateBatchTagCommand()
    {
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("batch-tag", "Add tags to many books")
        {
            inputOption, stdinOption, serverOption, tokenOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "ids and tags are both required and non-empty; max 1000 ids.",
            "",
            "Additive only: merges with existing tags, never removes one. To replace",
            "a set, use batch-update with tags.",
            "",
            "Exit 3 is HTTP 200 with a non-empty errors list — some ids did not",
            "resolve while the rest were tagged.");
        command.AddExamples(
            "grimoire-cli books batch-tag --input tags.json",
            "echo '{\"ids\":[\"<id>\"],\"tags\":[\"cyberpunk\"]}' | grimoire-cli books batch-tag --stdin");
        command.AddRequestShape<Generated.Models.BulkAddTags>();
        command.AddResponseExample<BulkTagResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.BulkAddTags.CreateFromDiscriminatorValue,
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
            var result = await new BooksService(client).BatchTagAsync(body);
            ConsoleOutput.WriteRawJson(result);
            return BulkExit.CodeFor(GrimoireApiClient.HasItems(result, "errors"));
        });
        return command;
    }

    private static Command CreateReindexCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var dpiOption = new Option<int?>("--ocr-dpi")
        {
            Description = "OCR resolution for this book (72-600); omit for the server default",
        };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("reindex", "Re-run OCR on one book")
        {
            idOption, dpiOption, serverOption, tokenOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "OCR only: 400 unless the book is an image-only PDF. A book with a real",
            "text layer has nothing to re-OCR.",
            "",
            "Clears the book's search index and re-queues it from page 1. The OCR",
            "runs in the background — watch it with:",
            "grimoire-cli library scan-status");
        command.AddExamples("grimoire-cli books reindex --id <book-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new BooksService(client);
            var response = await service.ReindexAsync(parseResult.GetValue(idOption)!, parseResult.GetValue(dpiOption));
            ConsoleOutput.WriteRawJson(response);
            return 0;
        });
        return command;
    }

    private static Command CreateRescanCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("rescan", "Re-read one book from disk and rebuild its index")
        {
            idOption, serverOption, tokenOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Re-reads the file and rebuilds the index, refreshing page count and",
            "thumbnail if the file changed. PDFs only: 400 on an epub or djvu, 404",
            "if the file is gone from disk.",
            "",
            "No-ops (silently skipped) under a library scan already running, and",
            "blocks a library rescan started right after it; the response is",
            "rescan_queued either way. Watch it with:",
            "grimoire-cli library scan-status");
        command.AddExamples("grimoire-cli books rescan --id <book-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new BooksService(client);
            var response = await service.RescanAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(response);
            return 0;
        });
        return command;
    }

    private static Command CreateThumbnailCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("thumbnail", "Download the book's cover thumbnail")
        {
            idOption, outputOption, serverOption, tokenOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The cover thumbnail generated from the file during a scan, not an",
            "uploaded image. 404 when has_thumbnail is false in books list.",
            "",
            "--output - writes the image to stdout; a path writes the file and",
            "prints {path, bytes}.");
        command.AddExamples(
            "grimoire-cli books thumbnail --id <id> --output cover.webp",
            "grimoire-cli books thumbnail --id <id> --output - > cover.webp");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new BooksService(client);
            await using var stream = await service.ThumbnailAsync(parseResult.GetValue(idOption)!);
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
}
