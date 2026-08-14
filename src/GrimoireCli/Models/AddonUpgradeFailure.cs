using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

// Cannot reuse BulkError: that DTO names the field "detail", this endpoint
// (addons/install.py:update_all) names it "error".
/// <summary>One failed add-on from POST /api/addons/update-all.</summary>
public class AddonUpgradeFailure
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
