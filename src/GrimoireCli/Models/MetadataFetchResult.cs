using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>POST /api/{systems,books}/{id}/metadata-fetch response. A report, not a write.</summary>
public class MetadataFetchResult
{
    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }

    [JsonPropertyName("identity")]
    public string? Identity { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("attribution")]
    public string? Attribution { get; set; }

    [JsonPropertyName("fields")]
    public List<MetadataFieldDiff>? Fields { get; set; }
}
