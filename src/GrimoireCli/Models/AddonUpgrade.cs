using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>One updated add-on from POST /api/addons/update-all (addons/install.py:update_all).</summary>
public class AddonUpgrade
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    // "from" is a C# keyword, so the property is named From with the wire
    // name restored via JsonPropertyName.
    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }
}
