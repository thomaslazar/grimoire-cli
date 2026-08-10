using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

public class GameSystemSummary
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("publishers")]
    public List<PublisherEntry>? Publishers { get; set; }

    [JsonPropertyName("character_builder_url")]
    public string? CharacterBuilderUrl { get; set; }

    [JsonPropertyName("character_builder_urls")]
    public List<LinkEntry>? CharacterBuilderUrls { get; set; }

    [JsonPropertyName("urls")]
    public List<LinkEntry>? Urls { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("genres")]
    public List<string>? Genres { get; set; }

    [JsonPropertyName("dice_materials")]
    public List<string>? DiceMaterials { get; set; }

    [JsonPropertyName("system_family")]
    public string? SystemFamily { get; set; }

    [JsonPropertyName("parent_system")]
    public string? ParentSystem { get; set; }

    [JsonPropertyName("edition")]
    public string? Edition { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("book_count")]
    public int BookCount { get; set; }

    [JsonPropertyName("total_page_count")]
    public int TotalPageCount { get; set; }

    [JsonPropertyName("cover_image")]
    public string? CoverImage { get; set; }

    [JsonPropertyName("cover_book_id")]
    public string? CoverBookId { get; set; }

    [JsonPropertyName("is_explicit")]
    public bool IsExplicit { get; set; }

    [JsonPropertyName("is_system_agnostic")]
    public bool IsSystemAgnostic { get; set; }

    [JsonPropertyName("is_one_page")]
    public bool IsOnePage { get; set; }
}
