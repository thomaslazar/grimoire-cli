using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>One ranked search hit (addons/interpreter.py:search).</summary>
public class MetadataCandidate
{
    /// <summary>The source's own key for this record; what metadata-fetch --identity takes.</summary>
    [JsonPropertyName("identity")]
    public string? Identity { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
