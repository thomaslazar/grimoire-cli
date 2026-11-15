using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class PinsCommand
{
    private static readonly string[] Collections = ["systems", "maps", "tokens", "audio", "models"];

    public static Command Create()
    {
        var command = new Command("pins", "Your pinned folders and game systems");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateAddCommand());
        command.Subcommands.Add(CreateRemoveCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var collectionOption = OptionHelpers.Choice("--collection", "Only this collection's pins", Collections);
        var command = new Command("list", "List your pins") { collectionOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, "Oldest first.");
        command.AddResponseExample<Generated.Models.PinsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new PinsService(client).ListAsync(parseResult.GetValue(collectionOption)));
            return 0;
        });
        return command;
    }

    private static Command CreateAddCommand()
    {
        var collectionOption = OptionHelpers.Choice("--collection", "Collection to pin in", Collections);
        collectionOption.Required = true;
        var targetOption = new Option<string>("--target") { Description = "A system id for systems; else a folder path in that collection", Required = true };
        var command = new Command("add", "Pin a folder or game system") { collectionOption, targetOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Leading and trailing slashes are stripped. Pinning something already",
            "pinned returns the existing pin. A collection holds at most 100 pins;",
            "past that is a 400.");
        command.AddExamples("grimoire-cli pins add --collection maps --target \"Dungeons/Caves\"");
        command.AddResponseExample<Generated.Models.PinOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new PinsService(client).AddAsync(
                parseResult.GetValue(collectionOption)!, parseResult.GetValue(targetOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateRemoveCommand()
    {
        var pinIdOption = new Option<string>("--pin-id") { Description = "Pin ID", Required = true };
        var command = new Command("remove", "Unpin") { pinIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, "Another user's pin is a 404, as a missing one is.");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new PinsService(client).RemoveAsync(parseResult.GetValue(pinIdOption)!));
            return 0;
        });
        return command;
    }
}
