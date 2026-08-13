using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

public class ScanStatus
{
    [JsonPropertyName("running")]
    public bool? Running { get; set; }

    // Null between scans; a non-nullable string would throw on it.
    [JsonPropertyName("phase")]
    public string? Phase { get; set; }

    [JsonPropertyName("total_books")]
    public int? TotalBooks { get; set; }

    [JsonPropertyName("scanned_books")]
    public int? ScannedBooks { get; set; }

    [JsonPropertyName("total_maps")]
    public int? TotalMaps { get; set; }

    [JsonPropertyName("scanned_maps")]
    public int? ScannedMaps { get; set; }

    [JsonPropertyName("total_tokens")]
    public int? TotalTokens { get; set; }

    [JsonPropertyName("scanned_tokens")]
    public int? ScannedTokens { get; set; }

    [JsonPropertyName("total_audio")]
    public int? TotalAudio { get; set; }

    [JsonPropertyName("scanned_audio")]
    public int? ScannedAudio { get; set; }

    [JsonPropertyName("new_books")]
    public int? NewBooks { get; set; }

    [JsonPropertyName("new_maps")]
    public int? NewMaps { get; set; }

    [JsonPropertyName("new_tokens")]
    public int? NewTokens { get; set; }

    [JsonPropertyName("new_audio")]
    public int? NewAudio { get; set; }

    [JsonPropertyName("updated_books")]
    public int? UpdatedBooks { get; set; }

    [JsonPropertyName("indexed")]
    public int? Indexed { get; set; }

    [JsonPropertyName("to_index")]
    public int? ToIndex { get; set; }

    // Deferred-OCR queue progress (phase "ocr"). TotalOcr = books queued,
    // OcrDone = books finished, OcrCurrent = filename in flight.
    [JsonPropertyName("total_ocr")]
    public int? TotalOcr { get; set; }

    [JsonPropertyName("ocr_done")]
    public int? OcrDone { get; set; }

    [JsonPropertyName("ocr_current")]
    public string? OcrCurrent { get; set; }
}
