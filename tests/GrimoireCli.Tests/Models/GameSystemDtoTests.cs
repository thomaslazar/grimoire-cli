using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class GameSystemDtoTests
{
    // A trimmed real response from the live instance: the field names must match
    // exactly or values silently deserialize to null.
    private const string SummaryJson = """
    {"id":"abc","name":"Shadowrun 6 DE","slug":"shadowrun-6-de","description":null,
     "publishers":[{"name":"Pegasus Spiele","url":""}],"character_builder_url":null,
     "character_builder_urls":[],"urls":[],"tags":[],"genre":"","genres":["Cyberpunk"],
     "dice_materials":[],"system_family":"Shadowrun","parent_system":"Shadowrun",
     "edition":"6","license":"","year":2019,"book_count":227,"total_page_count":6002,
     "cover_image":null,"cover_book_id":"xyz","is_explicit":false,
     "is_system_agnostic":false,"is_one_page":false}
    """;

    [Fact]
    public void SummaryDeserializesEveryScalarField()
    {
        var s = JsonSerializer.Deserialize(SummaryJson, AppJsonContext.Default.GameSystemSummary)!;
        Assert.Equal("abc", s.Id);
        Assert.Equal("Shadowrun 6 DE", s.Name);
        Assert.Equal("shadowrun-6-de", s.Slug);
        Assert.Equal("Shadowrun", s.SystemFamily);
        Assert.Equal("Shadowrun", s.ParentSystem);
        Assert.Equal("6", s.Edition);
        Assert.Equal(2019, s.Year);
        Assert.Equal(227, s.BookCount);
        Assert.Equal(6002, s.TotalPageCount);
        Assert.Equal("xyz", s.CoverBookId);
        Assert.False(s.IsExplicit);
        Assert.False(s.IsSystemAgnostic);
        Assert.False(s.IsOnePage);
    }

    [Fact]
    public void SummaryDeserializesNestedAndListFields()
    {
        var s = JsonSerializer.Deserialize(SummaryJson, AppJsonContext.Default.GameSystemSummary)!;
        Assert.Equal("Pegasus Spiele", Assert.Single(s.Publishers!).Name);
        Assert.Equal("Cyberpunk", Assert.Single(s.Genres!));
        Assert.Empty(s.Urls!);
        Assert.Empty(s.Tags!);
    }

    [Fact]
    public void SummaryRoundTripsToTheSameApiFieldNames()
    {
        var s = JsonSerializer.Deserialize(SummaryJson, AppJsonContext.Default.GameSystemSummary)!;
        // AppJsonContext has WriteIndented = true (set for config readability), so
        // serialized output has a space after each colon rather than compact JSON.
        var json = JsonSerializer.Serialize(s, AppJsonContext.Default.GameSystemSummary);
        Assert.Contains("\"book_count\": 227", json);
        Assert.Contains("\"system_family\": \"Shadowrun\"", json);
        Assert.Contains("\"is_one_page\": false", json);
        Assert.DoesNotContain("BookCount", json);
    }

    [Fact]
    public void DetailCarriesBooksOnTopOfTheSummary()
    {
        const string detail = """
        {"id":"abc","name":"Shadowrun 6 DE","book_count":1,"total_page_count":12,
         "is_explicit":false,"is_system_agnostic":false,"is_one_page":false,
         "books":[{"id":"b1","title":"SR6 Grundregelwerk","category":"core",
                   "page_count":12,"language":"","indexed":true,"index_failed":false,
                   "ocr_indexed":false,"has_thumbnail":false,"is_explicit":false,
                   "is_missing":false,"relative_path":"books/Shadowrun 6 DE/core/x.pdf"}]}
        """;
        var d = JsonSerializer.Deserialize(detail, AppJsonContext.Default.GameSystemDetail)!;
        Assert.Equal("Shadowrun 6 DE", d.Name);
        var book = Assert.Single(d.Books!);
        Assert.Equal("SR6 Grundregelwerk", book.Title);
        Assert.Equal("core", book.Category);
        Assert.Equal(12, book.PageCount);
        Assert.Equal("books/Shadowrun 6 DE/core/x.pdf", book.RelativePath);
    }

    [Fact]
    public void BookToleratesNullIndexedFlags()
    {
        // Indexed / index_failed / has_thumbnail come from nullable, uncoerced
        // columns upstream, so the API can send null instead of a boolean —
        // deserializing must not throw.
        const string bookJson = """
        {"id":"b1","title":"SR6 Grundregelwerk","indexed":null,"index_failed":null,
         "has_thumbnail":null,"ocr_indexed":false,"is_explicit":false,"is_missing":false}
        """;
        var book = JsonSerializer.Deserialize(bookJson, AppJsonContext.Default.Book)!;
        Assert.Null(book.Indexed);
        Assert.Null(book.IndexFailed);
        Assert.Null(book.HasThumbnail);
    }

    [Fact]
    public void BookRoundTripsNonNullIndexedFlags()
    {
        const string bookJson = """
        {"id":"b1","title":"SR6 Grundregelwerk","indexed":true,"index_failed":false,
         "has_thumbnail":true,"ocr_indexed":false,"is_explicit":false,"is_missing":false}
        """;
        var book = JsonSerializer.Deserialize(bookJson, AppJsonContext.Default.Book)!;
        Assert.True(book.Indexed);
        Assert.False(book.IndexFailed);
        Assert.True(book.HasThumbnail);
    }

    [Fact]
    public void ListOfSummariesDeserializes()
    {
        var list = JsonSerializer.Deserialize($"[{SummaryJson}]", AppJsonContext.Default.ListGameSystemSummary)!;
        Assert.Equal("Shadowrun 6 DE", Assert.Single(list).Name);
    }
}
