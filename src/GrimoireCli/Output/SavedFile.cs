using System.Text.Json.Serialization;

// Namespace deliberately kept as GrimoireCli.Models despite living in Output/ —
// tools/GenerateJsonExamples/Program.cs and JsonExamplesTests.cs reference
// GrimoireCli.Models.SavedFile by fully-qualified name.
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
