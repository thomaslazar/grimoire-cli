using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>An installed add-on, as serialised by addons/registry.py:describe().</summary>
public class AddonInstalled
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    [JsonPropertyName("attribution")]
    public string? Attribution { get; set; }

    [JsonPropertyName("blocked_reason")]
    public string? BlockedReason { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("available_version")]
    public string? AvailableVersion { get; set; }

    // describe() builds this dict fresh every call, so these booleans are
    // always present — never absent or null.
    [JsonPropertyName("requires_script")]
    public bool RequiresScript { get; set; }

    [JsonPropertyName("script_approved")]
    public bool ScriptApproved { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("runnable")]
    public bool Runnable { get; set; }

    [JsonPropertyName("update_available")]
    public bool UpdateAvailable { get; set; }
}
