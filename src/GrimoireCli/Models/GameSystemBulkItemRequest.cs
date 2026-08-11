using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// One item of POST /api/systems/bulk: the editable fields plus a required id
/// (routers/_bulk_schemas.py::bulk_update_model). Separate from
/// <see cref="GameSystemUpdateRequest"/> because the single-item body must not
/// carry an id at all — sharing one type would allow it where it is rejected, or
/// reject it where it is mandatory. The attribute is repeated because
/// JsonUnmappedMemberHandling does not inherit.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class GameSystemBulkItemRequest : GameSystemUpdateRequest
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }
}
