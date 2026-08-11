using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>Body of POST /api/systems/bulk. At most 1000 items (server-enforced).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class GameSystemBulkUpdateRequest
{
    [JsonPropertyName("items")]
    public required List<GameSystemBulkItemRequest> Items { get; set; }
}
