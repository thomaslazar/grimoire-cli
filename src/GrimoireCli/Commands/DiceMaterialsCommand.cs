using System.CommandLine;

namespace GrimoireCli.Commands;

public static class DiceMaterialsCommand
{
    public static Command Create()
    {
        var command = new Command("dice-materials", "The dice/material vocabulary");
        command.Subcommands.Add(VocabularyCommand.List(
            "dice-materials",
            "List all dice/materials",
            [
            "group buckets the entry, and is Custom when unset.",
            ],
            example => example.AddResponseExample<Generated.Models.DiceMaterialsResponse>()));
        return command;
    }
}
