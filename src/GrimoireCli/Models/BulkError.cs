using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>One skipped item from a bulk response (services/bulk_service.py:109).</summary>
public class BulkError
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}
