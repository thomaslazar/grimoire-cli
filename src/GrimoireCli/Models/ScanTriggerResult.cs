using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

// `library rescan` reads Status to choose exit code 3 on "already_running".
public class ScanTriggerResult
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }
}
