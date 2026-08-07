using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

public class LinkEntry
{
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
