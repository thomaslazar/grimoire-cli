using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// GET /api/systems/{id} — the summary shape plus the system's books. Filters on
/// that endpoint apply to the book list, and book_count / total_page_count are
/// recomputed from the filtered list.
/// </summary>
public class GameSystemDetail : GameSystemSummary
{
    [JsonPropertyName("books")]
    public List<Book>? Books { get; set; }
}
