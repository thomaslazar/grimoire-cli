using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class TagsCommand
{
    public static Command Create()
    {
        var command = new Command("tags", "Tags across every resource type");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateItemsCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var inUseByOption = new Option<string?>("--in-use-by") { Description = "Restrict to tags used on this resource type: system, book, map, token, audio" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("list", "List tags with their usage counts")
        {
            inUseByOption, serverOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Folder-derived tags are merged in and counted, and category is the",
            "effective one across every type a tag appears on. --in-use-by scopes",
            "the counts to that type.");
        command.AddExamples(
            "grimoire-cli tags list",
            "grimoire-cli tags list --in-use-by book");
        command.AddResponseExample<Generated.Models.TagsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new TagsService(client);
            var result = await service.ListAsync(parseResult.GetValue(inUseByOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateItemsCommand()
    {
        var tagOption = new Option<string>("--tag") { Description = "The tag's internal key, from tags list; matched case-insensitively", Required = true };
        var resourceTypeOption = new Option<string?>("--resource-type") { Description = "Restrict to this resource type: system, book, map, token, audio" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("items", "Items and folders carrying a tag")
        {
            tagOption, resourceTypeOption, serverOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Items carrying the tag directly are in items; those inheriting it",
            "from a folder tag are under folders.");
        command.AddExamples("grimoire-cli tags items --tag dungeon");
        command.AddResponseExample<Generated.Models.TagItemsResponse>();
        AddTaggedItemShapes(command);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new TagsService(client);
            var result = await service.ItemsAsync(
                parseResult.GetValue(tagOption)!,
                parseResult.GetValue(resourceTypeOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    /// <summary>
    /// Spells out the five shapes the response's items arrays hold. The
    /// generated sample renders the union as a bare list of type names, which
    /// names the branches without showing any of their fields. Private rather
    /// than a HelpExtensions helper: one command calls it.
    /// </summary>
    private static void AddTaggedItemShapes(Command command)
    {
        (string Type, Type Model)[] shapes =
        [
            ("book", typeof(Generated.Models.TaggedBookItem)),
            ("map", typeof(Generated.Models.TaggedMapItem)),
            ("token", typeof(Generated.Models.TaggedTokenItem)),
            ("audio", typeof(Generated.Models.TaggedAudioItem)),
            ("system", typeof(Generated.Models.TaggedSystemItem)),
        ];
        foreach (var (type, model) in shapes)
            command.AddShapeSection($"Item shape (item_type \"{type}\")", JsonExamples.For(model).Split('\n'));
    }
}
