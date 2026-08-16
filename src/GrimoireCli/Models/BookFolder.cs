using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>One subcategory folder under a system, in display casing.</summary>
public class BookFolder
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
}
