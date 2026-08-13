using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class BooksCommand
{
    public static Command Create()
    {
        var command = new Command("books", "Read and edit book metadata");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
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
            "--limit defaults to 100 and 422s above 500; page with --offset against",
            "the total in the response.",
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
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.BookListResponse);
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
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.BookDetail);
            return 0;
        });
        return command;
    }
}
