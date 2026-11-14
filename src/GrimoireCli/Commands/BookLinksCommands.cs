using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class BookLinksCommands
{
    public static Command Create()
    {
        var command = new Command("links", "Show a book on other systems' shelves too");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateAddCommand());
        command.Subcommands.Add(CreateRemoveCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var command = new Command("list", "Systems the book is linked onto")
        {
            idOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Never includes the book's own system; only systems you can see.");
        command.AddExamples("grimoire-cli books links list --id <book-id>");
        command.AddResponseExampleArray<Generated.Models.LinkedSystemRef>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new BookLinksService(client).ListAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateAddCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var systemIdOption = new Option<string>("--system-id") { Description = "System whose shelf gets the book", Required = true };
        var command = new Command("add", "Link the book onto another system's shelf")
        {
            idOption, systemIdOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "One file and one row, listed on both shelves; category, tags and",
            "access level still come from the book. Linking twice is a no-op.",
            "",
            "400 for the book's own system, a version of a book (link its main",
            "version) or a system across the books/ / armory/ divide.");
        command.AddExamples("grimoire-cli books links add --id <book-id> --system-id <system-id>");
        command.AddResponseExampleArray<Generated.Models.LinkedSystemRef>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new BookLinksService(client).AddAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(systemIdOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateRemoveCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var systemIdOption = new Option<string>("--system-id") { Description = "System to take the book off", Required = true };
        var command = new Command("remove", "Take the book off another system's shelf")
        {
            idOption, systemIdOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The book stays on its own system's shelf. 404 when it is not linked there.");
        command.AddExamples("grimoire-cli books links remove --id <book-id> --system-id <system-id>");
        command.AddResponseExampleArray<Generated.Models.LinkedSystemRef>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new BookLinksService(client).RemoveAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(systemIdOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
