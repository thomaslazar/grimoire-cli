using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// What one cleanup removed, per resource (routers/maintenance/_helpers.py:113).
/// systems counts folders pruned for having no books left, not systems asked for.
/// </summary>
public class CleanupCounts
{
    [JsonPropertyName("books")]
    public int Books { get; set; }

    [JsonPropertyName("maps")]
    public int Maps { get; set; }

    [JsonPropertyName("tokens")]
    public int Tokens { get; set; }

    [JsonPropertyName("audio")]
    public int Audio { get; set; }

    [JsonPropertyName("systems")]
    public int Systems { get; set; }
}
