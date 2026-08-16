using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>POST /api/systems/{id}/cover response — the stored filename.</summary>
public class CoverUploadResult
{
    [JsonPropertyName("cover_image")]
    public string? CoverImage { get; set; }
}
