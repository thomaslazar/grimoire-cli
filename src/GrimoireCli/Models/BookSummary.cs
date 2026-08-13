using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

public class BookSummary
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("page_count")]
    public int? PageCount { get; set; }

    [JsonPropertyName("file_size")]
    public int? FileSize { get; set; }

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; set; }

    [JsonPropertyName("game_system_id")]
    public string? GameSystemId { get; set; }

    [JsonPropertyName("has_thumbnail")]
    public bool? HasThumbnail { get; set; }

    [JsonPropertyName("indexed")]
    public bool? Indexed { get; set; }

    [JsonPropertyName("index_failed")]
    public bool? IndexFailed { get; set; }

    [JsonPropertyName("ocr_indexed")]
    public bool? OcrIndexed { get; set; }

    [JsonPropertyName("is_explicit")]
    public bool? IsExplicit { get; set; }

    [JsonPropertyName("is_missing")]
    public bool? IsMissing { get; set; }
}
