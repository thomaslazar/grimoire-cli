using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

public class Book
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("page_count")]
    public int? PageCount { get; set; }

    [JsonPropertyName("file_size")]
    public long? FileSize { get; set; }

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; set; }

    [JsonPropertyName("authors")]
    public List<string>? Authors { get; set; }

    [JsonPropertyName("artists")]
    public List<string>? Artists { get; set; }

    [JsonPropertyName("genres")]
    public List<string>? Genres { get; set; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }

    [JsonPropertyName("publisher_url")]
    public string? PublisherUrl { get; set; }

    [JsonPropertyName("urls")]
    public List<LinkEntry>? Urls { get; set; }

    [JsonPropertyName("isbn")]
    public string? Isbn { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("month")]
    public int? Month { get; set; }

    [JsonPropertyName("day")]
    public int? Day { get; set; }

    // Upstream emits these three raw (book.indexed, not bool(book.indexed)), and the
    // backing columns are nullable, so the API can send null. IsExplicit/IsMissing
    // below are bool()-coerced upstream and can't be null — don't "tidy" these back.
    [JsonPropertyName("indexed")]
    public bool? Indexed { get; set; }

    [JsonPropertyName("index_failed")]
    public bool? IndexFailed { get; set; }

    [JsonPropertyName("index_error")]
    public string? IndexError { get; set; }

    [JsonPropertyName("ocr_indexed")]
    public bool OcrIndexed { get; set; }

    [JsonPropertyName("ocr_dpi")]
    public int? OcrDpi { get; set; }

    // Same nullable column, no bool() coercion upstream — see the comment on Indexed.
    [JsonPropertyName("has_thumbnail")]
    public bool? HasThumbnail { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("is_explicit")]
    public bool IsExplicit { get; set; }

    [JsonPropertyName("is_missing")]
    public bool IsMissing { get; set; }

    [JsonPropertyName("relative_path")]
    public string? RelativePath { get; set; }
}
