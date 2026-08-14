using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>POST /api/addons/refresh response (routers/addons/core.py:refresh_index).</summary>
public class RefreshResult
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("count")]
    public int? Count { get; set; }
}
