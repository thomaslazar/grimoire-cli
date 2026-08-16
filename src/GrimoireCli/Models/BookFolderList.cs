using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>GET /api/systems/{id}/book-folders response.</summary>
public class BookFolderList
{
    [JsonPropertyName("folders")]
    public List<BookFolder>? Folders { get; set; }
}
