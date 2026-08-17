using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// Receipt for a binary body written to disk. Local to the CLI — no endpoint
/// returns this shape; it exists so a download still answers on stdout with JSON.
/// </summary>
public class SavedFile
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }
}
