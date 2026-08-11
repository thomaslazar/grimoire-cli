using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// Body of POST /api/systems/bulk/tags. Both lists are required and must be
/// non-empty; ids is capped at 1000 (routers/_bulk_schemas.py::BulkAddTags).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class BulkAddTagsRequest
{
    [JsonPropertyName("ids")]
    public required List<string> Ids { get; set; }

    [JsonPropertyName("tags")]
    public required List<string> Tags { get; set; }
}
