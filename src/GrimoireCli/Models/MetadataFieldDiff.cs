using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// One field's comparison (addons/diff.py:build). current and incoming are typed
/// by the field they describe — a string, an int, a list of strings, or a list of
/// objects — so they are carried as JsonElement and re-emitted verbatim.
/// </summary>
public class MetadataFieldDiff
{
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("current")]
    public JsonElement? Current { get; set; }

    [JsonPropertyName("incoming")]
    public JsonElement? Incoming { get; set; }

    /// <summary>only_incoming, differs, or same.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }
}
