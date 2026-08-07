using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

public class PublisherEntry
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
