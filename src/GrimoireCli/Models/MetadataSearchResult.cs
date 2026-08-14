using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>POST /api/{systems,books}/{id}/metadata-search response.</summary>
public class MetadataSearchResult
{
    /// <summary>The query actually searched, after the server's fallback to the
    /// system's name or the book's title.</summary>
    [JsonPropertyName("query")]
    public string? Query { get; set; }

    [JsonPropertyName("results")]
    public List<MetadataCandidate>? Results { get; set; }
}
