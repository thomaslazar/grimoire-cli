using GrimoireCli.Models;

namespace GrimoireCli.Commands;

/// <summary>
/// Maps a bulk response to an exit code. 3 means HTTP 200 with items skipped —
/// distinct from 2 (the request failed) and 1 (a client-side refusal), because an
/// unattended caller has to tell "nothing was applied" from "most of it was".
/// </summary>
public static class BulkExit
{
    public static int CodeFor(List<BulkError>? errors) => errors is { Count: > 0 } ? 3 : 0;
}
