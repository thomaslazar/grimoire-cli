using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// Request-side publisher entry (routers/systems/_schemas.py:10-12). Separate from
/// <see cref="PublisherEntry"/> because Disallow does not propagate into element
/// types: without a strict type here, a typo inside publishers would be sent.
/// Marking the response DTO strict instead would make reads fail on a field a
/// newer Grimoire adds.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class PublisherEntryRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
