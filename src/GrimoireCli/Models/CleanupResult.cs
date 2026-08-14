using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>POST /api/maintenance/cleanup-missing response.</summary>
public class CleanupResult
{
    [JsonPropertyName("removed")]
    public CleanupCounts? Removed { get; set; }
}
