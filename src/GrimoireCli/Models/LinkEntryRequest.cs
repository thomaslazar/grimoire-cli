using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// Request-side labeled link (routers/systems/_schemas.py:15-19), used by both
/// urls and character_builder_urls. Strict for the reason given on
/// <see cref="PublisherEntryRequest"/>.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class LinkEntryRequest
{
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
