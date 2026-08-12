using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// Response of POST /api/systems/bulk/tags. tags maps each updated id to its full
/// display-tag set after the merge, so the caller sees the result without refetching.
/// </summary>
public class BulkTagResult : BulkUpdateResult
{
    [JsonPropertyName("tags")]
    public Dictionary<string, List<string>>? Tags { get; set; }
}
