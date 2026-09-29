using System.CommandLine;
using System.Text.Json;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignResourcesCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly string[] OwnerOnly =
    [
        "Owner only, no admin override. 403 also when the owner's campaign",
        "access is disabled or the campaign is archived.",
    ];
    private static readonly string[] ResourceTypes = ["book", "map", "token", "audio", "model"];
    private static readonly string[] Visibilities = ["gm", "public", "private"];

    public static Command Create()
    {
        var command = new Command("resources", "Library items linked into a campaign");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateAddCommand());
        command.Subcommands.Add(CreateBulkCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateRemoveCommand());
        command.Subcommands.Add(CreateReorderCommand());
        return command;
    }

    /// <summary>
    /// Whether bulk created fewer rows than it was sent. The endpoint has no errors
    /// field — it drops duplicates and unknown types silently — so the counts are
    /// the only signal. Internal so a test can pin it.
    /// </summary>
    internal static bool SkippedAny(string body, string response)
    {
        using var sent = JsonDocument.Parse(body);
        using var created = JsonDocument.Parse(response);
        return created.RootElement.GetArrayLength() < sent.RootElement.GetProperty("resources").GetArrayLength();
    }

    private static Command CreateListCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var command = new Command("list", "List a campaign's linked items") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Ordered by visibility (public, private, gm), then sort_order, then name.",
            "id is the link id that update, remove and reorder take.",
            "",
            "Owner or accepted member; members see only what visibility allows.");
        command.AddExamples("grimoire-cli campaigns resources list --id <campaign-id>");
        command.AddResponseExampleArray<Generated.Models.LinkedResourceOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignResourcesService(client).ListAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateAddCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var resourceTypeOption = OptionHelpers.Choice("--resource-type", "Kind of library item", ResourceTypes);
        resourceTypeOption.Required = true;
        var resourceIdOption = new Option<string>("--resource-id") { Description = "The library item's ID", Required = true };
        var visibilityOption = OptionHelpers.Choice("--visibility", "Who sees it; default gm (owner only)", Visibilities);
        var categoryIdOption = new Option<string?>("--category-id") { Description = "Resource category to file it under" };
        var command = new Command("add", "Link one library item into a campaign")
        {
            idOption, resourceTypeOption, resourceIdOption, visibilityOption, categoryIdOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. OwnerOnly, "",
            "The item id is not checked: an unknown id links a row named by the id.",
            "A model link's name is always its id.",
            "",
            "A restricted book is forced to gm whatever --visibility says. private",
            "shows it to the owner alone; share lists go through bulk.",
            "",
            "409 when the item is already linked. Uploaded files link themselves:",
            "grimoire-cli campaigns files upload."]);
        command.AddExamples("grimoire-cli campaigns resources add --id <campaign-id> --resource-type book --resource-id <book-id> --visibility public");
        command.AddResponseExample<Generated.Models.LinkedResourceOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new CampaignResourcesService(client).AddAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(resourceTypeOption)!,
                parseResult.GetValue(resourceIdOption)!,
                parseResult.GetValue(visibilityOption),
                parseResult.GetValue(categoryIdOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateBulkCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("bulk", "Link many library items in one call") { idOption, inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. OwnerOnly, "",
            "Duplicates and unknown resource_type values are skipped silently; the",
            "response lists only the links created. Exit 3 when fewer come back than",
            "were sent; stdout still carries them.",
            "",
            "A visibility outside gm|public|private becomes gm. file is not linkable",
            "here. Item ids are not checked."]);
        command.AddExamples("echo '{\"resources\":[{\"resource_type\":\"book\",\"resource_id\":\"<id>\"}]}' | grimoire-cli campaigns resources bulk --id <campaign-id> --stdin");
        command.AddRequestShape<Generated.Models.ResourceBulkAdd>();
        command.AddResponseExampleArray<Generated.Models.LinkedResourceOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.ResourceBulkAdd.CreateFromDiscriminatorValue, "put it in each item");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            var result = await new CampaignResourcesService(client).BulkAsync(parseResult.GetValue(idOption)!, body);
            ConsoleOutput.WriteRawJson(result);
            return BulkExit.CodeFor(SkippedAny(body, result));
        });
        return command;
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var linkIdOption = new Option<string>("--link-id") { Description = "Link ID from resources list", Required = true };
        var visibilityOption = OptionHelpers.Choice("--visibility", "Who sees it", Visibilities);
        var categoryIdOption = new Option<string?>("--category-id") { Description = "Resource category; \"\" moves it back to its type group" };
        var command = new Command("update", "Change a link's visibility or category")
        {
            idOption, linkIdOption, visibilityOption, categoryIdOption
        };
        command.Validators.Add(result =>
        {
            var hasVisibility = result.GetValue(visibilityOption) is not null;
            var hasCategoryId = result.GetValue(categoryIdOption) is not null;
            if (!hasVisibility && !hasCategoryId)
                result.AddError("Provide --visibility or --category-id.");
        });
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. OwnerOnly, "",
            "Any visibility but private clears the share list. A restricted book",
            "stays gm."]);
        command.AddResponseExample<Generated.Models.LinkedResourceOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new CampaignResourcesService(client).UpdateAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(linkIdOption)!,
                parseResult.GetValue(visibilityOption),
                parseResult.GetValue(categoryIdOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateRemoveCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var linkIdOption = new Option<string>("--link-id") { Description = "Link ID from resources list", Required = true };
        var command = new Command("remove", "Unlink an item from a campaign") { idOption, linkIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. OwnerOnly, "",
            "The library item is untouched, but removing a file link deletes the upload.",
            "",
            "Answers 204: stdout carries no body."]);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new CampaignResourcesService(client).RemoveAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(linkIdOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateReorderCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var orderedIdsOption = new Option<string[]>("--ordered-ids")
        {
            Description = "Link IDs in the new order",
            AllowMultipleArgumentsPerToken = true,
            Required = true,
        };
        var command = new Command("reorder", "Set the manual order of a campaign's links") { idOption, orderedIdsOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. OwnerOnly, "",
            "Pass every link id: unknown ones are skipped and unlisted ones keep their",
            "old position. Order holds only within a visibility group."]);
        command.AddExamples("grimoire-cli campaigns resources reorder --id <campaign-id> --ordered-ids <link-id> <link-id>");
        command.AddResponseExample<Generated.Models.Backend__routers__campaigns___response_schemas__OkResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new CampaignResourcesService(client).ReorderAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(orderedIdsOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
