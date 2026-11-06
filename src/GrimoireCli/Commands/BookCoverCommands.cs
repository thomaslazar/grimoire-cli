using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class BookCoverCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("cover", "The book's custom cover image");
        command.Subcommands.Add(CreateUploadCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateFromSourceCommand());
        return command;
    }

    private static Command CreateUploadCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var fileOption = new Option<string>("--file") { Description = "Path to a PNG, JPEG, WebP or GIF", Required = true };
        var command = new Command("upload", "Upload a custom cover for the book")
        {
            idOption, fileOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Shown in place of the generated thumbnail, and a rescan never replaces",
            "it. Replaces any custom cover; stored as WebP at most 900px wide.",
            "",
            "The server checks the content type first, then the size, so an",
            "oversized image (over 10 MB) answers 413 and an unsupported one 400.",
            "An image over 40 megapixels is also a 400.",
            "",
            "Rewrites the book's .cover.jpg sidecar if it already has one.");
        command.AddExamples("grimoire-cli books cover upload --id <book-id> --file cover.png");
        command.AddResponseExample<Generated.Models.BookCoverResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new BooksService(client);
            try
            {
                var result = await service.UploadCoverAsync(
                    parseResult.GetValue(idOption)!,
                    parseResult.GetValue(fileOption)!);
                ConsoleOutput.WriteRawJson(result);
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

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var command = new Command("delete", "Remove the book's custom cover")
        {
            idOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The generated thumbnail is shown again. Succeeds whether or not a",
            "custom cover was set.",
            "",
            "Rewrites the book's .cover.jpg sidecar if it already has one.");
        command.AddExamples("grimoire-cli books cover delete --id <book-id>");
        command.AddResponseExample<Generated.Models.BookCoverResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new BooksService(client);
            var result = await service.DeleteCoverAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateFromSourceCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var sourceTypeOption = new Option<string>("--source-type") { Description = "The kind of library item to copy the image from", Required = true };
        var sourceIdOption = new Option<string>("--source-id") { Description = "That item's ID", Required = true };
        var command = new Command("from-source", "Set the book's cover from an image already in the library")
        {
            idOption, sourceTypeOption, sourceIdOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--source-type takes map, token, book or audio; campaign_file is not a",
            "valid value here (422) since a book has no campaign to resolve it",
            "against.",
            "",
            "The bytes are copied, so the cover survives the source going away.",
            "Replaces any custom cover.");
        command.AddExamples(
            "grimoire-cli books cover from-source --id <book-id> --source-type map --source-id <map-id>");
        command.AddResponseExample<Generated.Models.BookCoverResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new BooksService(client);
            var result = await service.CoverFromSourceAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(sourceTypeOption)!,
                parseResult.GetValue(sourceIdOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
