using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class MapVttCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("vtt", "Universal VTT data and exports");
        command.Subcommands.Add(CreateImageCommand());
        command.Subcommands.Add(CreateDataCommand());
        command.Subcommands.Add(CreateExportCommand());
        return command;
    }

    private static Command CreateImageCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Map ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var command = new Command("image", "Download the battlemap embedded in a Universal VTT file")
        {
            idOption, outputOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "400 unless the map is a .uvtt or .dd2vtt carrying an image; the",
            "base64 envelope is decoded server-side.",
            "",
            "--output - writes the image to stdout; a path writes it and prints",
            "{path, bytes}.");
        command.AddExamples("grimoire-cli maps vtt image --id <id> --output battlemap.png");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            await using var stream = await service.VttImageAsync(parseResult.GetValue(idOption)!);
            try
            {
                await ConsoleOutput.WriteStreamAsync(stream, parseResult.GetValue(outputOption)!);
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            return 0;
        });
        return command;
    }

    private static Command CreateDataCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Map ID", Required = true };
        var command = new Command("data", "Grid and feature counts from a Universal VTT file")
        {
            idOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "400 unless the map is a .uvtt or .dd2vtt. The embedded image is",
            "omitted — maps vtt image serves it.");
        command.AddExamples("grimoire-cli maps vtt data --id <id>");
        command.AddResponseExample<Generated.Models.VttDataResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var result = await service.VttDataAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateExportCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Map ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var command = new Command("export", "Export a raster map as a Universal VTT file")
        {
            idOption, outputOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Builds the .uvtt on demand: the image as base64 WebP, the grid, and",
            "any walls, portals and lights authored in the app. Nothing is written",
            "into the library.",
            "",
            "400 for a PDF, video or archive map, and for a raster already linked",
            "to a Universal VTT file — that sibling carries the geometry.",
            "",
            "--output - writes the file to stdout; a path writes it and prints",
            "{path, bytes}.");
        command.AddExamples("grimoire-cli maps vtt export --id <id> --output tavern.uvtt");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            await using var stream = await service.VttExportAsync(parseResult.GetValue(idOption)!);
            try
            {
                await ConsoleOutput.WriteStreamAsync(stream, parseResult.GetValue(outputOption)!);
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            return 0;
        });
        return command;
    }
}
