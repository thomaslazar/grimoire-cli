using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>One index row, as built by routers/addons/core.py:list_addons().</summary>
public class AddonAvailable
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    [JsonPropertyName("script_sha256")]
    public string? ScriptSha256 { get; set; }

    // This dict is built fresh from the cached index on every call, so these
    // booleans are always present — never absent or null.
    [JsonPropertyName("requires_script")]
    public bool RequiresScript { get; set; }

    [JsonPropertyName("installed")]
    public bool Installed { get; set; }

    [JsonPropertyName("update_available")]
    public bool UpdateAvailable { get; set; }
}
