using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// PATCH /api/systems/{id}/book-folders response. Its tags are internal keys,
/// not the display casing GET returns, so it is a separate type from
/// <see cref="BookFolder"/> rather than a reused one.
/// </summary>
public class BookFolderUpdated
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
}
