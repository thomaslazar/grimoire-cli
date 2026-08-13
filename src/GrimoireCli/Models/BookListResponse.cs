using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

public class BookListResponse
{
    [JsonPropertyName("total")]
    public int? Total { get; set; }

    [JsonPropertyName("books")]
    public List<BookSummary>? Books { get; set; }
}
