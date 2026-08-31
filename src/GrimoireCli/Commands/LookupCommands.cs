using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

/// <summary>
/// The five controlled-vocabulary reads, one top-level group each — the shape
/// abs-cli settled for its own genres / tags / narrators. Every endpoint is a
/// parameterless GET with no role dependency, so the five commands differ only
/// in the vocabulary they name and one line of Notes; hence one table rather
/// than five near-identical files.
/// </summary>
public static class LookupCommands
{
    /// <summary>
    /// Caveats shared by all five. Neither is recoverable from the response
    /// sample, which shows id and name side by side without saying which one a
    /// write takes and says nothing about validation.
    /// </summary>
    private static readonly string[] SharedNotes =
    [
        "Submit name, not id — systems and books store the name. id addresses the",
        "vocabulary entry itself.",
        "",
        "Nothing validates a written value against this list: an unmatched string",
        "is stored as written and stops matching systems list --genre.",
        "",
    ];

    private sealed record Vocabulary(
        string Name,
        string GroupDescription,
        string ListDescription,
        string[] Notes,
        Action<Command> AddResponseExample);

    private static readonly Vocabulary[] Vocabularies =
    [
        new("genres", "The genre vocabulary", "List all genres (tiered)",
            ["parent_id links a child to its parent. Ordered by sort_order, then name."],
            command => command.AddResponseExample<Generated.Models.GenresResponse>()),
        new("licenses", "The license vocabulary", "List all licenses",
            ["is_default false is a custom entry."],
            command => command.AddResponseExample<Generated.Models.LicensesResponse>()),
        new("parent-systems", "The parent-system vocabulary", "List all parent systems",
            [
                "Ships empty: Grimoire seeds no defaults, and a container child's",
                "parent_system is folder-derived, so a value in use need not appear here.",
            ],
            command => command.AddResponseExample<Generated.Models.ParentSystemsResponse>()),
        new("system-families", "The system-family vocabulary", "List all system families",
            ["is_default false is a custom entry."],
            command => command.AddResponseExample<Generated.Models.SystemFamiliesResponse>()),
        new("dice-materials", "The dice/material vocabulary", "List all dice/materials",
            ["group buckets the entry, and is Custom when unset."],
            command => command.AddResponseExample<Generated.Models.DiceMaterialsResponse>()),
    ];

    public static IEnumerable<Command> Create()
    {
        foreach (var vocabulary in Vocabularies)
        {
            var group = new Command(vocabulary.Name, vocabulary.GroupDescription);
            group.Subcommands.Add(CreateListCommand(vocabulary));
            yield return group;
        }
    }

    private static Command CreateListCommand(Vocabulary vocabulary)
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("list", vocabulary.ListDescription) { serverOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, [.. SharedNotes, .. vocabulary.Notes]);
        command.AddExamples($"grimoire-cli {vocabulary.Name} list");
        vocabulary.AddResponseExample(command);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new LookupsService(client);
            var result = await service.ListAsync(vocabulary.Name);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
