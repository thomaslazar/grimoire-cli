using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// Response of POST /api/systems/bulk. Skip-and-continue: an unresolved id or a
/// rejected item lands in errors while the rest still apply, so a non-empty errors
/// list is a partial application, not a failure. An id in updated means the row
/// resolved, not that any value changed.
/// </summary>
public class BulkUpdateResult
{
    [JsonPropertyName("updated")]
    public List<string>? Updated { get; set; }

    [JsonPropertyName("errors")]
    public List<BulkError>? Errors { get; set; }
}
