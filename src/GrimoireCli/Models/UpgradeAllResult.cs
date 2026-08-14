using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>POST /api/addons/update-all response (routers/addons/core.py:update_all_addons).</summary>
public class UpgradeAllResult
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("updated")]
    public List<AddonUpgrade>? Updated { get; set; }

    [JsonPropertyName("failed")]
    public List<AddonUpgradeFailure>? Failed { get; set; }
}
