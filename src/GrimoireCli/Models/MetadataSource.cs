using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>One add-on able to supply metadata (routers/_metadata_lookup.py:list_sources).</summary>
public class MetadataSource
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    [JsonPropertyName("attribution")]
    public string? Attribution { get; set; }

    /// <summary>True when the manifest declares an identity_pattern, which is what
    /// makes metadata-fetch --paste resolvable for this source.</summary>
    [JsonPropertyName("supports_paste")]
    public bool SupportsPaste { get; set; }
}
