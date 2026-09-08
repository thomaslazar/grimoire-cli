using System.CommandLine;
using GrimoireCli.Api;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class DuplicatesCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    internal static readonly string[] ResourceTypes = ["book", "map", "token", "audio"];

    public static Command Create()
    {
        var command = new Command("duplicates", "Find and resolve duplicate library items");
        command.Subcommands.Add(CreateLinkCommand());
        command.Subcommands.Add(CreatePromoteCommand());
        command.Subcommands.Add(CreateUnlinkCommand());
        command.Subcommands.Add(CreateMergeMetadataCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateCompareCommand());
        foreach (var leaf in DuplicatesScanCommands.Create())
            command.Subcommands.Add(leaf);
        return command;
    }

    private static Command CreateLinkCommand()
    {
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("link", "File items under a parent as its variants")
        {
            inputOption, stdinOption, serverOption
        };
        command.AddRoleRequired("admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Body: {resource_type, parent_id, children:[{id, kind, label}]}. At most 20",
            "children per request.",
            "",
            "Per child: a rejected one lands in errors and the rest are linked, so a",
            "partial exits 3. Re-sending a fixed batch re-reports the children that",
            "already linked.",
            "",
            "A child cannot be its own parent, cannot be in another collection, and",
            "cannot already be a variant — variants are two levels deep, never three.",
            "",
            "kind by collection. book: version, other, printer-friendly, form-fillable,",
            "spreads, single-page, black-and-white. map: version, other,",
            "printer-friendly, black-and-white, gridded, gridless, universal-vtt, video,",
            "image. token: version, other, black-and-white, color-variation. audio:",
            "version, other, remix, slowed, sped-up.",
            "",
            "label is free text, trimmed to 120 characters without warning.");
        command.AddExamples(
            "grimoire-cli duplicates link --input link.json",
            "echo '{\"resource_type\":\"book\",\"parent_id\":\"<id>\",\"children\":[{\"id\":\"<id>\",\"kind\":\"printer-friendly\",\"label\":\"A4 print\"}]}' | grimoire-cli duplicates link --stdin");
        command.AddRequestShape<Generated.Models.LinkRequest>();
        command.AddResponseExample<Generated.Models.LinkResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.LinkRequest.CreateFromDiscriminatorValue,
                    "put it in children");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var result = await new DuplicatesService(client).LinkAsync(body);
            ConsoleOutput.WriteRawJson(result);
            return BulkExit.CodeFor(GrimoireApiClient.HasItems(result, "errors"));
        });
        return command;
    }

    private static Command CreatePromoteCommand()
    {
        var resourceTypeOption = OptionHelpers.Choice("--resource-type", "Collection to act on", ResourceTypes);
        resourceTypeOption.Required = true;
        var newParentIdOption = new Option<string>("--new-parent-id") { Description = "Item to promote", Required = true };
        var oldParentIdOption = new Option<string>("--old-parent-id") { Description = "Item to demote", Required = true };
        var kindOption = new Option<string?>("--kind") { Description = "The old parent's kind once demoted; default other" };
        var labelOption = new Option<string?>("--label") { Description = "The old parent's label once demoted" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("promote", "Make a different copy the main version of a family")
        {
            resourceTypeOption, newParentIdOption, oldParentIdOption, kindOption, labelOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--kind and --label describe the old parent, which becomes a variant.",
            "",
            "One indivisible change: the old parent and every child move together.",
            "--new-parent-id must not already be a variant of something else, and",
            "--old-parent-id must not itself be a variant.");
        command.AddExamples(
            "grimoire-cli duplicates promote --resource-type book --new-parent-id <id> --old-parent-id <id> --kind version --label \"2015 printing\"");
        command.AddResponseExample<Generated.Models.PromoteResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var result = await new DuplicatesService(client).PromoteAsync(
                parseResult.GetValue(resourceTypeOption)!,
                parseResult.GetValue(newParentIdOption)!,
                parseResult.GetValue(oldParentIdOption)!,
                parseResult.GetValue(kindOption),
                parseResult.GetValue(labelOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateUnlinkCommand()
    {
        var resourceTypeOption = OptionHelpers.Choice("--resource-type", "Collection to act on", ResourceTypes);
        resourceTypeOption.Required = true;
        var idsOption = new Option<string[]>("--ids")
        {
            Description = "Variants to free; repeatable",
            AllowMultipleArgumentsPerToken = true,
        };
        var parentIdOption = new Option<string?>("--parent-id") { Description = "Free every variant of this parent" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("unlink", "Promote variants back to standalone entries")
        {
            resourceTypeOption, idsOption, parentIdOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.Validators.Add(result =>
        {
            var hasIds = result.GetValue(idsOption) is { Length: > 0 };
            var hasParentId = result.GetValue(parentIdOption) != null;
            if (!hasIds && !hasParentId)
                result.AddError("Provide --ids or --parent-id.");
        });
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--parent-id frees every variant of that parent and wins if both are given.",
            "",
            "The files are untouched; only the parent link goes.");
        command.AddExamples(
            "grimoire-cli duplicates unlink --resource-type book --ids <id> <id>",
            "grimoire-cli duplicates unlink --resource-type book --parent-id <id>");
        command.AddResponseExample<Generated.Models.UnlinkResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var result = await new DuplicatesService(client).UnlinkAsync(
                parseResult.GetValue(resourceTypeOption)!,
                parseResult.GetValue(idsOption) ?? [],
                parseResult.GetValue(parentIdOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateMergeMetadataCommand()
    {
        var resourceTypeOption = OptionHelpers.Choice("--resource-type", "Collection to act on", ResourceTypes);
        resourceTypeOption.Required = true;
        var sourceIdOption = new Option<string>("--source-id") { Description = "Item to copy from", Required = true };
        var targetIdOption = new Option<string>("--target-id") { Description = "Item to copy onto", Required = true };
        var fieldsOption = new Option<string[]>("--fields")
        {
            Description = "Fields to copy; repeatable",
            Required = true,
            AllowMultipleArgumentsPerToken = true,
        };
        var overwriteOption = new Option<bool>("--overwrite") { Description = "Replace values already set on the target" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("merge-metadata", "Copy metadata fields from one copy onto another")
        {
            resourceTypeOption, sourceIdOption, targetIdOption, fieldsOption, overwriteOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Without --overwrite a field is copied only where the target's is empty;",
            "everything else comes back in skipped, which is not an error. An empty",
            "source field is skipped either way. tags are always additive.",
            "",
            "--fields for a book: title, description, authors, artists, publisher,",
            "publisher_url, urls, genres, isbn, version, language, license, year, month,",
            "day, category, is_explicit, tags. map: description, map_type, grid_size,",
            "tags. token: description, is_explicit, tags. audio: description, title,",
            "artist, album, tags. Anything else is refused with the collection's set.");
        command.AddExamples(
            "grimoire-cli duplicates merge-metadata --resource-type book --source-id <id> --target-id <id> --fields title description",
            "grimoire-cli duplicates merge-metadata --resource-type book --source-id <id> --target-id <id> --fields tags --overwrite");
        command.AddResponseExample<Generated.Models.MergeMetadataResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var result = await new DuplicatesService(client).MergeMetadataAsync(
                parseResult.GetValue(resourceTypeOption)!,
                parseResult.GetValue(sourceIdOption)!,
                parseResult.GetValue(targetIdOption)!,
                parseResult.GetValue(fieldsOption)!,
                parseResult.GetValue(overwriteOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var resourceTypeOption = OptionHelpers.Choice("--resource-type", "Collection to act on", ResourceTypes);
        resourceTypeOption.Required = true;
        var idOption = new Option<string>("--id") { Description = "Item to delete", Required = true };
        var deleteFileOption = new Option<bool>("--delete-file")
        {
            Description = "Also delete the file from disk; irreversible",
            Required = true,
            Arity = ArgumentArity.ExactlyOne,
        };
        var reparentToOption = new Option<string?>("--reparent-to") { Description = "Which variant inherits the rest; \"\" frees them all" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("delete", "Delete one duplicate's record, and optionally its file")
        {
            resourceTypeOption, idOption, deleteFileOption, reparentToOption, serverOption
        };
        command.AddRoleRequired("admin");
        // Option<bool>.Required isn't enforced by System.CommandLine — bool's own
        // default (false) satisfies it — so a bodyless run would otherwise fall
        // through to the server's destructive delete_file default silently.
        command.Validators.Add(result =>
        {
            if (result.GetResult(deleteFileOption) is null)
                result.AddError("Option '--delete-file' is required.");
        });
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The record goes either way, with its bookmarks, favorites, tags, campaign",
            "links, indexed page text and thumbnail. --delete-file decides only whether",
            "the file leaves the disk, with its sidecars.",
            "",
            "--delete-file has no default here: files delete spells the same flag and",
            "defaults to keeping the file, so this one is stated every time.",
            "",
            "An item that has variants answers 409 unless --reparent-to is given: \"\"",
            "frees them all, an id names which of them inherits the rest.",
            "",
            "A record whose file is already gone still deletes, reporting",
            "file_deleted: false. A read-only library answers 409 and changes nothing.");
        command.AddExamples(
            "grimoire-cli duplicates delete --resource-type book --id <id> --delete-file true",
            "grimoire-cli duplicates delete --resource-type book --id <id> --delete-file true --reparent-to \"\"");
        command.AddResponseExample<Generated.Models.DeleteItemResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var result = await new DuplicatesService(client).DeleteItemAsync(
                parseResult.GetValue(resourceTypeOption)!,
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(deleteFileOption),
                parseResult.GetValue(reparentToOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateCompareCommand()
    {
        var resourceTypeOption = OptionHelpers.Choice("--resource-type", "Collection to act on", ResourceTypes);
        resourceTypeOption.Required = true;
        var idsOption = new Option<string[]>("--ids")
        {
            Description = "Items to compare; repeatable",
            Required = true,
            AllowMultipleArgumentsPerToken = true,
        };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("compare", "Compare two to four copies side by side")
        {
            resourceTypeOption, idsOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Two to four --ids; anything else is refused.",
            "",
            "reference_counts per item is the user work attached to that copy, and",
            "suggested_parent_id is the server's pick for which to keep.");
        command.AddExamples(
            "grimoire-cli duplicates compare --resource-type book --ids <id> <id>");
        command.AddResponseExample<Generated.Models.CompareResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var result = await new DuplicatesService(client).CompareAsync(
                parseResult.GetValue(resourceTypeOption)!,
                parseResult.GetValue(idsOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
