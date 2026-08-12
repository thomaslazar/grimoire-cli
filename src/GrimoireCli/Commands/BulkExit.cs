using GrimoireCli.Models;

namespace GrimoireCli.Commands;

/// <summary>
/// Maps a bulk response to an exit code. 3 means the request succeeded (HTTP 200)
/// and at least one item was skipped, distinct from 2 (the request itself failed)
/// and 1 (a client-side refusal before any request was made). stdout still carries
/// the full `updated`/`errors` JSON, so an unattended caller can act on which items
/// landed and which didn't — including the all-skipped case, which is also a 3.
/// </summary>
public static class BulkExit
{
    public static int CodeFor(List<BulkError>? errors) => errors is { Count: > 0 } ? 3 : 0;
}
