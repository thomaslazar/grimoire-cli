using System.CommandLine;

namespace GrimoireCli.Commands;

public static class ParentSystemsCommand
{
    public static Command Create()
    {
        var command = new Command("parent-systems", "The parent-system vocabulary");
        command.Subcommands.Add(VocabularyCommand.List(
            "parent-systems",
            "List all parent systems",
            [
            "Ships empty: Grimoire seeds no defaults, and a container child's",
            "parent_system is folder-derived, so a value in use need not appear here.",
            ],
            example => example.AddResponseExample<Generated.Models.ParentSystemsResponse>()));
        return command;
    }
}
