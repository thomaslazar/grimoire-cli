using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class MapsCommand
{
    public static Command Create()
    {
        var command = new Command("maps", "Read and edit map metadata");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var mapTypeOption = new Option<string?>("--map-type") { Description = "Filter by map type" };
        var folderOption = new Option<string?>("--folder") { Description = "Filter by exact folder path" };
        var limitOption = OptionHelpers.Range("--limit", "Results per page (default 100; the server sets no maximum)", 1);
        var offsetOption = OptionHelpers.Range("--offset", "Items to skip", 0);
        var command = new Command("list", "List maps")
        {
            mapTypeOption, folderOption, limitOption, offsetOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--limit defaults to 100 here; the server sets no ceiling, so a larger",
            "page is just a larger number.",
            "",
            "--folder is an exact folder, not a subtree: battlemaps excludes",
            "battlemaps/caves. Values are the folder part of relative_path.",
            "",
            "Variants are hidden — only the main copy of a family is listed.");
        command.AddExamples(
            "grimoire-cli maps list",
            "grimoire-cli maps list --folder battlemaps --limit 20",
            "grimoire-cli maps list --map-type battlemap --offset 100");
        command.AddResponseExample<Generated.Models.MapListResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var result = await service.ListAsync(
                parseResult.GetValue(mapTypeOption),
                parseResult.GetValue(folderOption),
                parseResult.GetValue(limitOption) ?? 100,
                parseResult.GetValue(offsetOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Map ID", Required = true };
        var command = new Command("get", "Get one map")
        {
            idOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "grid is what detection found, with its own source; grid_width,",
            "grid_height and grid_px are a manual override. All three null means",
            "detection is in charge.");
        command.AddExamples("grimoire-cli maps get --id <map-id>");
        command.AddResponseExample<Generated.Models.MapDetailResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var result = await service.GetAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
