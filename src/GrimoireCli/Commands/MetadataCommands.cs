using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

/// <summary>
/// The add-on metadata trio, built once and added to both systems and books.
/// The endpoints are one implementation upstream against two targets
/// (routers/_metadata_lookup.py), and differ here only in the resource noun and
/// the fallback the server substitutes for an empty query.
/// </summary>
public static class MetadataCommands
{
    public static IEnumerable<Command> Create(string resource)
    {
        var fallback = resource == "systems" ? "name" : "title";
        yield return CreateSourcesCommand(resource);
        yield return CreateSearchCommand(resource, fallback);
        yield return CreateFetchCommand(resource, fallback);
    }

    private static Option<string> IdOption(string resource) =>
        new("--id") { Description = resource == "systems" ? "System ID" : "Book ID", Required = true };

    private static Command CreateSourcesCommand(string resource)
    {
        var idOption = IdOption(resource);
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("metadata-sources", "List add-ons that can supply metadata")
        {
            idOption, serverOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Add-ons that can answer for this resource. Empty until one is installed,",
            "enabled and runnable (addons list) and targets this resource type — a",
            "book source never appears here for a system.",
            "",
            "supports_paste false means metadata-fetch --paste is a 400 for that",
            "source; search for an identity instead.");
        command.AddExamples($"grimoire-cli {resource} metadata-sources --id <id>");
        command.AddResponseExample<Generated.Models.MetadataSourcesResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new MetadataService(client, resource);
            var result = await service.SourcesAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateSearchCommand(string resource, string fallback)
    {
        var idOption = IdOption(resource);
        var sourceIdOption = new Option<string>("--source-id")
        {
            Description = "Source add-on ID, from metadata-sources",
            Required = true,
        };
        var queryOption = new Option<string?>("--query") { Description = $"Search text; defaults to the {fallback}" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("metadata-search", "Search one add-on for candidates")
        {
            idOption, sourceIdOption, queryOption, serverOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Candidates only — identity, label, score, url. No field data; that is",
            "metadata-fetch.",
            "",
            $"An omitted --query defaults to the {fallback}; query echoes back what was",
            "actually searched. Pass the same value to metadata-fetch: search-backed",
            "sources answer per query, not from a catalogue.",
            "",
            "[] means the source matched nothing. 502 means it could not be reached",
            "or returned junk; 400 a configuration one, such as an unknown --source-id.");
        command.AddExamples($"grimoire-cli {resource} metadata-search --id <id> --source-id <source>");
        command.AddResponseExample<Generated.Models.MetadataSearchResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new MetadataService(client, resource);
            var result = await service.SearchAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(sourceIdOption)!,
                parseResult.GetValue(queryOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateFetchCommand(string resource, string fallback)
    {
        var idOption = IdOption(resource);
        var sourceIdOption = new Option<string>("--source-id")
        {
            Description = "Source add-on ID, from metadata-sources",
            Required = true,
        };
        var identityOption = new Option<string?>("--identity") { Description = "Candidate identity, from metadata-search" };
        var queryOption = new Option<string?>("--query") { Description = "Query the candidate came from; required for search-backed sources" };
        var pasteOption = new Option<string?>("--paste") { Description = "Source URL or bare ID, instead of --identity" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("metadata-fetch", "Diff one candidate against this resource")
        {
            idOption, sourceIdOption, identityOption, queryOption, pasteOption, serverOption
        };
        command.AddRoleRequired("gm or admin");
        command.Validators.Add(result =>
        {
            var hasIdentity = result.GetValue(identityOption) is not null;
            var hasPaste = result.GetValue(pasteOption) is not null;
            if (hasIdentity == hasPaste)
                result.AddError("Pass exactly one of --identity or --paste.");
        });
        var linkFields = resource == "systems" ? "urls and character_builder_urls" : "urls";
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Writes nothing. Reports, per field, what this resource has now and what",
            $"the source offers; apply what you want with {resource} update.",
            "",
            "Exactly one of --identity (from metadata-search) or --paste (a source",
            "URL or bare ID, only where supports_paste is true).",
            "",
            "status is only_incoming (empty here), differs, or same, sorted in that",
            "order. A field the source has nothing for is omitted, so nothing is ever",
            $"proposed to be blanked. incoming for {linkFields} is",
            "the union with the existing list, not a replacement.",
            "",
            "502 is a source failure, 400 a configuration one.");
        command.AddExamples($"grimoire-cli {resource} metadata-fetch --id <id> --source-id <source> --identity <identity>");
        command.AddResponseExample<Generated.Models.MetadataFetchResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new MetadataService(client, resource);
            var result = await service.FetchAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(sourceIdOption)!,
                parseResult.GetValue(identityOption),
                parseResult.GetValue(queryOption),
                parseResult.GetValue(pasteOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
