using System.CommandLine;

namespace GrimoireCli.Commands;

public static class GenresCommand
{
    public static Command Create()
    {
        var command = new Command("genres", "The genre vocabulary");
        command.Subcommands.Add(VocabularyCommand.List(
            "genres",
            "List all genres (tiered)",
            [
            "parent_id links a child to its parent. Ordered by sort_order, then name.",
            ],
            example => example.AddResponseExample<Generated.Models.GenresResponse>()));
        return command;
    }
}
