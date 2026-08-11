using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// Body of PATCH /api/systems/{id} — the 17 editable fields of Grimoire's
/// GameSystemUpdate (routers/systems/_schemas.py:56-75), and nothing else.
/// Deserializing a body into this type is the only check made before sending:
/// Grimoire drops unknown keys and answers {"status":"ok"}, so a misspelled field
/// would otherwise report success having changed nothing. The type is the field
/// list, so there is no separate list to drift.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class GameSystemUpdateRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("publishers")]
    public List<PublisherEntryRequest>? Publishers { get; set; }

    // Legacy single; new clients send character_builder_urls.
    [JsonPropertyName("character_builder_url")]
    public string? CharacterBuilderUrl { get; set; }

    [JsonPropertyName("character_builder_urls")]
    public List<LinkEntryRequest>? CharacterBuilderUrls { get; set; }

    [JsonPropertyName("urls")]
    public List<LinkEntryRequest>? Urls { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    // Legacy single; new clients send genres.
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

    [JsonPropertyName("cover_book_id")]
    public string? CoverBookId { get; set; }

    [JsonPropertyName("is_explicit")]
    public bool? IsExplicit { get; set; }
}
