using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>GET /api/{systems,books}/{id}/metadata-sources response.</summary>
public class MetadataSourceList
{
    [JsonPropertyName("sources")]
    public List<MetadataSource>? Sources { get; set; }
}
