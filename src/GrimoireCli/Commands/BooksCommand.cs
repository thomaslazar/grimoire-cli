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
        command.Subcommands.Add(CreateTocCommand());
        command.Subcommands.Add(CreatePageTextCommand());
        command.Subcommands.Add(CreatePageWordsCommand());
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
        var command = new Command("list", "List books (defaults to 100 results)")
        {
            systemIdOption, categoryOption, limitOption, offsetOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Rows are a reduced shape — no tags, language, isbn, authors, artists,",
            "genres, urls or description. systems get returns every book in one",
            "system carrying all of them, in one call.",
            "",
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
        command.AddResponseExample<Generated.Models.BookListResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
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
        var command = new Command("get", "Get one book")
        {
            idOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "403 if the book is explicit and the account disallows explicit content.");
        command.AddExamples("grimoire-cli books get --id <book-id>");
        command.AddResponseExample<Generated.Models.BookDetail>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
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
        var command = new Command("update", "Update one book's metadata")
        {
            idOption, inputOption, stdinOption
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
            "grimoire-cli books get --id <id>",
            "",
            "genres and license draw on vocabularies: genres list, licenses list.",
            "Submit the name, not the id. Nothing validates against them — an",
            "unmatched value is stored as written.");
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
            var (client, _) = CommandHelper.BuildClient();
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
        var command = new Command("batch-update", "Update many books in one transaction")
        {
            inputOption, stdinOption
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
        command.AddResponseExample<Generated.Models.BulkResult>();
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
            var (client, _) = CommandHelper.BuildClient();
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
        var command = new Command("batch-tag", "Add tags to many books")
        {
            inputOption, stdinOption
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
        command.AddResponseExample<Generated.Models.BulkTagResult>();
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
            var (client, _) = CommandHelper.BuildClient();
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
        var command = new Command("reindex", "Re-run OCR on one book")
        {
            idOption, dpiOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "OCR only: 400 unless the book is an image-only PDF. A book with a real",
            "text layer has nothing to re-OCR.",
            "",
            "Clears the book's search index and re-queues it from page 1. The OCR",
            "runs in the background — watch it with:",
            "grimoire-cli library scan-status",
            "",
            "books get reports ocr_pages_skipped: above 0 the book is indexed but",
            "only partly searchable, those pages having timed out. Re-running resets",
            "the count and retries them. Requires Grimoire 1.7.1.");
        command.AddExamples("grimoire-cli books reindex --id <book-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
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
        var command = new Command("rescan", "Re-read one book from disk and rebuild its index")
        {
            idOption
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
            var (client, _) = CommandHelper.BuildClient();
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
        var command = new Command("thumbnail", "Download the book's cover thumbnail")
        {
            idOption, outputOption
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
            var (client, _) = CommandHelper.BuildClient();
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

    private static Command CreateTocCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var command = new Command("toc", "The book's table of contents")
        {
            idOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "PDF outlines and EPUB nav documents both work; 404 for a format",
            "that cannot be opened, which is indistinguishable from an unknown",
            "id.",
            "",
            "Entries nest through children, and page is where the entry points.");
        command.AddExamples("grimoire-cli books toc --id <book-id>");
        command.AddResponseExample<Generated.Models.TocResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new BooksService(client);
            var result = await service.TocAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreatePageTextCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var pageOption = new Option<int>("--page") { Description = "1-based page number", Required = true };
        var command = new Command("page-text", "The text of one page")
        {
            idOption, pageOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Served from the search index when the page has a row, else",
            "extracted live — so a scan yields what OCR found, and nothing",
            "until indexed is true in books get.",
            "",
            "Plain-text and Markdown books are readable too, not just PDF.",
            "",
            "A page outside the book is 400 with the real count; books get",
            "reports page_count. 404 also covers a file missing from disk.");
        command.AddExamples(
            "grimoire-cli books page-text --id <book-id> --page 241",
            "grimoire-cli search --query \"grappling\" | jq '.results[0].page_number'");
        command.AddResponseExample<Generated.Models.PageTextResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new BooksService(client);
            var result = await service.PageTextAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(pageOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreatePageWordsCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var pageOption = new Option<int>("--page") { Description = "1-based page number", Required = true };
        var command = new Command("page-words", "Word bounding boxes for one page")
        {
            idOption, pageOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Boxes are in PDF points against the width and height reported",
            "alongside them — for locating text on a rendered page, not for",
            "reading it. Use books page-text to read.",
            "",
            "Only PDF, EPUB and DjVu carry word boxes. Anything else — a text",
            "book, a comic — answers 200 with width 0 and no words, so an empty",
            "result does not mean the page is blank.");
        command.AddExamples("grimoire-cli books page-words --id <book-id> --page 241");
        command.AddResponseExample<Generated.Models.PageWordsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new BooksService(client);
            var result = await service.PageWordsAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(pageOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
