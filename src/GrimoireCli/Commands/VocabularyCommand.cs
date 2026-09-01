using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

/// <summary>
/// The `list` verb shared by the five controlled-vocabulary groups. Each group
/// owns its own file, as `systems cover` and `books folders` do; this holds the
/// one verb whose body is identical across all five, since every vocabulary read
/// is a parameterless GET with no role dependency
/// (`routers/lookups/core.py` guards each with `get_current_user`).
/// </summary>
internal static class VocabularyCommand
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
        "is stored as written. Where systems list filters on the field (--genre,",
        "--family, --parent-system, --license), an unmatched value stops matching.",
        "",
    ];

    /// <param name="vocabulary">Group name, which is also the path segment and the service key.</param>
    /// <param name="notes">The caveats specific to this vocabulary, appended to the shared ones.</param>
    /// <param name="addResponseExample">Registers the group's own response model, which is generic per vocabulary.</param>
    internal static Command List(
        string vocabulary, string description, string[] notes, Action<Command> addResponseExample)
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("list", description) { serverOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, [.. SharedNotes, .. notes]);
        command.AddExamples($"grimoire-cli {vocabulary} list");
        addResponseExample(command);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new LookupsService(client);
            var result = await service.ListAsync(vocabulary);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
