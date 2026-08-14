using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>GET /api/addons — installed add-ons plus what the cached index offers.</summary>
public class AddonListResponse
{
    [JsonPropertyName("installed")]
    public List<AddonInstalled>? Installed { get; set; }

    [JsonPropertyName("available")]
    public List<AddonAvailable>? Available { get; set; }

    [JsonPropertyName("index_url")]
    public string? IndexUrl { get; set; }

    [JsonPropertyName("default_index_url")]
    public string? DefaultIndexUrl { get; set; }

    [JsonPropertyName("allow_scripts")]
    public bool AllowScripts { get; set; }

    [JsonPropertyName("index_generated")]
    public string? IndexGenerated { get; set; }
}
