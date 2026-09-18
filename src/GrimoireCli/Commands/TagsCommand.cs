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
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateRenameCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateMergeCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var inUseByOption = new Option<string?>("--in-use-by") { Description = "Restrict to tags used on this resource type (system | book | map | token | audio | model)" };
        var command = new Command("list", "List tags with their usage counts")
        {
            inUseByOption
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
            var (client, _) = CommandHelper.BuildClient();
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
        var resourceTypeOption = new Option<string?>("--resource-type") { Description = "Restrict to this resource type (system | book | map | token | audio | model)" };
        var command = new Command("items", "Items and folders carrying a tag")
        {
            tagOption, resourceTypeOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Items carrying the tag directly are in items; those inheriting it",
            "from a folder tag are under folders.");
        command.AddExamples("grimoire-cli tags items --tag dungeon");
        command.AddResponseExample<Generated.Models.TagItemsResponse>();
        AddTaggedItemShapes(command);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new TagsService(client);
            var result = await service.ItemsAsync(
                parseResult.GetValue(tagOption)!,
                parseResult.GetValue(resourceTypeOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateCreateCommand()
    {
        var valueOption = new Option<string>("--value") { Description = "The tag text; its internal key is this lowercased", Required = true };
        var displayOption = new Option<string?>("--display") { Description = "Display casing, when it must differ from --value" };
        var command = new Command("create", "Create a tag up front")
        {
            valueOption, displayOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Idempotent: an existing tag is returned unchanged. Tags are also",
            "created on first use, so this is only needed to reserve one.",
            "",
            "'/' and '\\' are rejected: the key addresses the tag in the path.",
            "",
            "category is the resource type the tag is first used on, so a tag",
            "created here and not yet applied reads as shared.");
        command.AddExamples("grimoire-cli tags create --value \"GM Screen\"");
        command.AddResponseExample<Generated.Models.TagCreatedResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new TagsService(client);
            var result = await service.CreateAsync(
                parseResult.GetValue(valueOption)!,
                parseResult.GetValue(displayOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateRenameCommand()
    {
        var tagOption = new Option<string>("--tag") { Description = "The tag's current internal key, from tags list", Required = true };
        var displayOption = new Option<string>("--display") { Description = "The new display value", Required = true };
        var command = new Command("rename", "Rename a tag")
        {
            tagOption, displayOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The internal key follows the new display when its lowercased form",
            "changes, and folder tags are rewritten onto the new key. If another",
            "tag already owns that key, the two are merged and the survivor is",
            "returned — there is no warning and no undo.",
            "",
            "A tag that exists only on a folder is materialised first, so the new",
            "display survives a rescan.",
            "",
            "'/' and '\\' are rejected in the new display.");
        command.AddExamples(
            "grimoire-cli tags rename --tag gm-screen --display \"GM Screen\"",
            "grimoire-cli tags rename --tag freinds --display friends");
        command.AddResponseExample<Generated.Models.TagRenamedResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new TagsService(client);
            var result = await service.RenameAsync(
                parseResult.GetValue(tagOption)!,
                parseResult.GetValue(displayOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var tagOption = new Option<string>("--tag") { Description = "The tag's internal key, from tags list", Required = true };
        var command = new Command("delete", "Delete a tag everywhere")
        {
            tagOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Unlinks the tag from every item and strips it from every folder.",
            "This cannot be undone, and there is no confirmation prompt.",
            "",
            "A folder tag that came from a tags.json returns on the next rescan;",
            "the library is read-only, so the file itself is not rewritten.",
            "",
            "Answers 204: stdout carries no body.");
        command.AddExamples("grimoire-cli tags delete --tag gm-screen");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new TagsService(client);
            var result = await service.DeleteAsync(parseResult.GetValue(tagOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateMergeCommand()
    {
        var tagOption = new Option<string>("--tag") { Description = "The tag to merge away, by internal key", Required = true };
        var intoOption = new Option<string>("--into") { Description = "The surviving tag's key; created if it does not exist", Required = true };
        var command = new Command("merge", "Merge one tag into another")
        {
            tagOption, intoOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Re-points every item link, then deletes --tag. Items already",
            "carrying both keep one link.",
            "",
            "Folder tags are not re-pointed: a folder carrying --tag still",
            "carries it afterwards, so the merged tag can reappear in tags list.",
            "Use tags rename to move a folder-only tag.",
            "",
            "404 when --tag has no item links at all, even if a folder carries",
            "it. '/' and '\\' are rejected in --into but allowed in --tag, so a",
            "tag that predates that rule can be merged out of trouble.");
        command.AddExamples("grimoire-cli tags merge --tag \"D&D\" --into dungeons-and-dragons");
        command.AddResponseExample<Generated.Models.TagRenamedResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new TagsService(client);
            var result = await service.MergeAsync(
                parseResult.GetValue(tagOption)!,
                parseResult.GetValue(intoOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    /// <summary>
    /// Spells out the six shapes the response's items arrays hold. The
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
            ("model", typeof(Generated.Models.TaggedModelItem)),
            ("system", typeof(Generated.Models.TaggedSystemItem)),
        ];
        foreach (var (type, model) in shapes)
            command.AddShapeSection($"Item shape (item_type \"{type}\")", JsonExamples.For(model).Split('\n'));
    }
}
