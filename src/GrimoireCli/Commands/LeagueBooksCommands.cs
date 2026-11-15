using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class LeagueBooksCommands
{
    private const string Organiser = "Organiser or admin only.";
    private const string ResourceIdNote = "--resource-id is the id from books list, not the book_id.";
    private static readonly string[] Visibilities = ["public", "private", "organizer"];

    public static Command Create()
    {
        var command = new Command("books", "Library books shared with a league");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateSearchCommand());
        command.Subcommands.Add(CreateAddCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateRemoveCommand());
        command.Subcommands.Add(CreateReorderCommand());
        return command;
    }

    private static Option<string> LeagueIdOption() =>
        new("--id") { Description = "League ID", Required = true };

    private static Option<string> ResourceIdOption() =>
        new("--resource-id") { Description = "Shared book's id from books list", Required = true };

    private static Option<string[]?> SharedUserIdsOption(string description) =>
        new("--shared-user-ids")
        {
            Description = description,
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.OneOrMore,
        };

    // An unsupplied array option reads as empty, so presence is checked on the parse result.
    private static string[]? Shared(ParseResult parseResult, Option<string[]?> option) =>
        parseResult.GetResult(option) is null ? null : parseResult.GetValue(option);

    private static Command CreateListCommand()
    {
        var idOption = LeagueIdOption();
        var command = new Command("list", "List the books shared with a league") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "id is the shared book's id for update, remove and reorder; book_id is",
            "the library book. A member sees public books plus private ones shared",
            "with them.");
        command.AddExamples("grimoire-cli leagues books list --id <league-id>");
        command.AddResponseExampleArray<Generated.Models.LeagueBook>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeagueBooksService(client).ListAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateSearchCommand()
    {
        var idOption = LeagueIdOption();
        var queryOption = new Option<string>("--query") { Description = "Title text, 2 to 200 characters", Required = true };
        var command = new Command("search", "Find library books to share with a league") { idOption, queryOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            Organiser,
            "Books of any game type, the league's own system first, at most 20.",
            "Books already shared are not excluded.");
        command.AddExamples("grimoire-cli leagues books search --id <league-id> --query codex");
        command.AddResponseExampleArray<Generated.Models.BookSearchResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeagueBooksService(client).SearchAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(queryOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateAddCommand()
    {
        var idOption = LeagueIdOption();
        var bookIdOption = new Option<string>("--book-id") { Description = "Library book ID", Required = true };
        var visibilityOption = OptionHelpers.Choice("--visibility", "Who sees it; default organizer", Visibilities);
        var sharedOption = SharedUserIdsOption("League members to share a private book with");
        var command = new Command("add", "Share a library book with a league") { idOption, bookIdOption, visibilityOption, sharedOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            Organiser,
            "A restricted book becomes organizer whatever is asked: check visibility",
            "in the response.",
            "409 when already shared.",
            "--shared-user-ids applies only to private and must be league members, else 400.");
        command.AddExamples("grimoire-cli leagues books add --id <league-id> --book-id <book-id> --visibility public");
        command.AddResponseExample<Generated.Models.LeagueBook>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeagueBooksService(client).AddAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(bookIdOption)!,
                parseResult.GetValue(visibilityOption),
                Shared(parseResult, sharedOption)));
            return 0;
        });
        return command;
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = LeagueIdOption();
        var resourceIdOption = ResourceIdOption();
        var visibilityOption = OptionHelpers.Choice("--visibility", "Who sees it", Visibilities);
        var sharedOption = SharedUserIdsOption("League members the private book is shared with; replaces the current set");
        var command = new Command("update", "Change a shared book's visibility or shares") { idOption, resourceIdOption, visibilityOption, sharedOption };
        command.Validators.Add(result =>
        {
            if (result.GetValue(visibilityOption) is null && result.GetResult(sharedOption) is null)
                result.AddError("Provide --visibility or --shared-user-ids.");
        });
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            Organiser,
            ResourceIdNote,
            "Any visibility but private drops all shares. A restricted book stays organizer.",
            "Shares must be league members, else 400.");
        command.AddExamples("grimoire-cli leagues books update --id <league-id> --resource-id <id> --visibility public");
        command.AddResponseExample<Generated.Models.LeagueBook>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeagueBooksService(client).UpdateAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(resourceIdOption)!,
                parseResult.GetValue(visibilityOption),
                Shared(parseResult, sharedOption)));
            return 0;
        });
        return command;
    }

    private static Command CreateRemoveCommand()
    {
        var idOption = LeagueIdOption();
        var resourceIdOption = ResourceIdOption();
        var command = new Command("remove", "Stop sharing a book with a league") { idOption, resourceIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            Organiser,
            ResourceIdNote,
            "The library book is untouched.");
        command.AddExamples("grimoire-cli leagues books remove --id <league-id> --resource-id <id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeagueBooksService(client).RemoveAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(resourceIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateReorderCommand()
    {
        var idOption = LeagueIdOption();
        var orderedIdsOption = new Option<string[]>("--ordered-ids")
        {
            Description = "Shared book ids from books list, in the new order",
            AllowMultipleArgumentsPerToken = true,
            Required = true,
        };
        var command = new Command("reorder", "Set the manual order of a league's books") { idOption, orderedIdsOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            Organiser,
            "--ordered-ids takes the id from books list, not the book_id.",
            "Unknown ids are skipped and unlisted ones keep their old position.",
            "Order holds only within a visibility group.");
        command.AddExamples("grimoire-cli leagues books reorder --id <league-id> --ordered-ids <id> <id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new LeagueBooksService(client).ReorderAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(orderedIdsOption)!));
            return 0;
        });
        return command;
    }
}
