using System.CommandLine;

namespace GrimoireCli.Commands;

public static class LicensesCommand
{
    public static Command Create()
    {
        var command = new Command("licenses", "The license vocabulary");
        command.Subcommands.Add(VocabularyCommand.List(
            "licenses",
            "List all licenses",
            [
            "is_default false is a custom entry.",
            ],
            example => example.AddResponseExample<Generated.Models.LicensesResponse>()));
        return command;
    }
}
