using System.CommandLine;

namespace GrimoireCli.Commands;

public static class SystemFamiliesCommand
{
    public static Command Create()
    {
        var command = new Command("system-families", "The system-family vocabulary");
        command.Subcommands.Add(VocabularyCommand.List(
            "system-families",
            "List all system families",
            [
            "is_default false is a custom entry.",
            ],
            example => example.AddResponseExample<Generated.Models.SystemFamiliesResponse>()));
        return command;
    }
}
