using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>PATCH /api/addons/settings response (routers/addons/core.py:update_addon_settings).</summary>
public class AddonSettings
{
    [JsonPropertyName("index_url")]
    public string? IndexUrl { get; set; }

    [JsonPropertyName("allow_scripts")]
    public bool AllowScripts { get; set; }
}
