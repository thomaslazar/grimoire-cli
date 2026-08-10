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

    [JsonPropertyName("has_cover")]
    public bool HasCover { get; set; }

    // System containers (upstream #261/#262). A container is a folder whose
    // immediate children are systems rather than categories: "" for an ordinary
    // system, "parent" for a parent-system container whose subfolders are
    // editions, "one-page" for a one-page collection.
    [JsonPropertyName("container_kind")]
    public string? ContainerKind { get; set; }

    [JsonPropertyName("parent_id")]
    public string? ParentId { get; set; }

    [JsonPropertyName("parent_name")]
    public string? ParentName { get; set; }

    [JsonPropertyName("parent_is_one_page")]
    public bool ParentIsOnePage { get; set; }

    // True once a user renames the system in the UI, after which the scanner
    // stops overwriting the name on rescan.
    [JsonPropertyName("name_is_custom")]
    public bool NameIsCustom { get; set; }

    [JsonPropertyName("child_count")]
    public int ChildCount { get; set; }
}
