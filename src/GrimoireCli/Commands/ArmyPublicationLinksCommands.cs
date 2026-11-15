using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class ArmyPublicationLinksCommands
{
    public static Command Create()
    {
        var command = new Command("publication-links", "Link roster publications to library books");
        command.Subcommands.Add(CreateSetCommand());
        command.Subcommands.Add(CreateRemoveCommand());
        command.Subcommands.Add(CreateBooksCommand());
        return command;
    }

    private static Option<string> PublicationOption() => new("--publication")
    {
        Description = "Publication name as the roster cites it",
        Required = true,
    };

    private static Option<string?> SystemNameOption() => new("--system-name")
    {
        Description = "Roster's game-system name (system_name in armies get)",
    };

    private static Command CreateSetCommand()
    {
        var publicationOption = PublicationOption();
        var bookIdOption = new Option<string>("--book-id") { Description = "Library book ID", Required = true };
        var systemNameOption = SystemNameOption();
        var pageOffsetOption = new Option<int>("--page-offset")
        {
            Description = "Added to the cited page (-10000 to 10000)",
            DefaultValueFactory = _ => 0,
        };
        var command = new Command("set", "Link a publication to a book")
        {
            publicationOption, bookIdOption, systemNameOption, pageOffsetOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Upsert keyed on --system-name (case-insensitive) plus --publication. Every",
            "army citing it under that system shares the one link. 404 for a book you",
            "can't see.");
        command.AddExamples("grimoire-cli armies publication-links set --publication \"Codex: Space Marines\" --book-id <book-id> --system-name \"Warhammer 40,000\" --page-offset 2");
        command.AddResponseExample<Generated.Models.PublicationLinkOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new ArmyPublicationLinksService(client).SetAsync(
                parseResult.GetValue(publicationOption)!,
                parseResult.GetValue(bookIdOption)!,
                parseResult.GetValue(systemNameOption),
                parseResult.GetValue(pageOffsetOption)));
            return 0;
        });
        return command;
    }

    private static Command CreateRemoveCommand()
    {
        var publicationOption = PublicationOption();
        var systemNameOption = SystemNameOption();
        var command = new Command("remove", "Remove a publication's link") { publicationOption, systemNameOption };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Succeeds even when no link existed.");
        command.AddExamples("grimoire-cli armies publication-links remove --publication \"Codex: Space Marines\" --system-name \"Warhammer 40,000\"");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new ArmyPublicationLinksService(client).RemoveAsync(
                parseResult.GetValue(publicationOption)!, parseResult.GetValue(systemNameOption)));
            return 0;
        });
        return command;
    }

    private static Command CreateBooksCommand()
    {
        var queryOption = new Option<string>("--query") { Description = "Title text, 2 to 200 characters", Required = true };
        var command = new Command("books", "Find wargame books to link") { queryOption };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Wargame books only, at most 10.");
        command.AddExamples("grimoire-cli armies publication-links books --query codex");
        command.AddResponseExampleArray<Generated.Models.BookMatch>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new ArmyPublicationLinksService(client).BooksAsync(parseResult.GetValue(queryOption)!));
            return 0;
        });
        return command;
    }
}
