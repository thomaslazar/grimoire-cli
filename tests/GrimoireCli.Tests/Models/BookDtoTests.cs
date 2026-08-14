using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class BookDtoTests
{
    [Fact]
    public void BookListResponseRoundTripsTheEnvelope()
    {
        const string json = """
        {"total": 227, "books": [{"id": "b1", "title": "Core Rules", "category": "core",
         "game_system_id": "s1", "page_count": 320, "is_explicit": false, "is_missing": false}]}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.BookListResponse)!;
        Assert.Equal(227, result.Total);
        var book = Assert.Single(result.Books!);
        Assert.Equal("b1", book.Id);
        Assert.Equal("s1", book.GameSystemId);
        Assert.Equal(320, book.PageCount);
    }

    [Fact]
    public void BookDetailReadsItsNestedSystemAndTags()
    {
        const string json = """
        {"id": "b1", "title": "Core Rules", "authors": ["A"], "tags": ["crunchy"],
         "year": 2019, "month": 3, "day": 1, "ocr_pending": false,
         "game_system": {"id": "s1", "name": "Shadowrun 6 DE", "slug": "shadowrun-6-de"}}
        """;
        var book = JsonSerializer.Deserialize(json, AppJsonContext.Default.BookDetail)!;
        Assert.Equal("A", Assert.Single(book.Authors!));
        Assert.Equal("crunchy", Assert.Single(book.Tags!));
        Assert.Equal(2019, book.Year);
        Assert.Equal("shadowrun-6-de", book.GameSystem!.Slug);
    }

    // A book with no system has game_system: null, not an empty object.
    [Fact]
    public void BookDetailAcceptsANullSystem()
    {
        var book = JsonSerializer.Deserialize("""{"id": "b1", "game_system": null}""",
            AppJsonContext.Default.BookDetail)!;
        Assert.Null(book.GameSystem);
    }

    [Fact]
    public void ScanStatusReadsTheCountersAndTheOcrQueue()
    {
        const string json = """
        {"running": true, "phase": "ocr", "total_books": 12, "scanned_books": 5,
         "new_books": 2, "updated_books": 1, "indexed": 4, "to_index": 8,
         "total_ocr": 3, "ocr_done": 1, "ocr_current": "scan.pdf"}
        """;
        var status = JsonSerializer.Deserialize(json, AppJsonContext.Default.ScanStatus)!;
        Assert.True(status.Running);
        Assert.Equal("ocr", status.Phase);
        Assert.Equal(5, status.ScannedBooks);
        Assert.Equal("scan.pdf", status.OcrCurrent);
    }

    // phase is null between scans; a non-nullable string would throw on it.
    [Fact]
    public void ScanStatusAcceptsANullPhase()
    {
        var status = JsonSerializer.Deserialize("""{"running": false, "phase": null}""",
            AppJsonContext.Default.ScanStatus)!;
        Assert.False(status.Running);
        Assert.Null(status.Phase);
    }

    [Fact]
    public void ScanTriggerResultReadsItsStatus()
    {
        var result = JsonSerializer.Deserialize("""{"status": "already_running"}""",
            AppJsonContext.Default.ScanTriggerResult)!;
        Assert.Equal("already_running", result.Status);
    }

    // SQLite's INTEGER storage class is always 8 bytes regardless of the
    // declared column width, so a multi-gigabyte scanned book's file_size
    // can exceed int.MaxValue on the wire. A narrower type would throw.
    [Fact]
    public void BookSummaryFileSizeSurvivesBeyondIntRange()
    {
        const string json = """{"id": "b1", "file_size": 5000000000}""";
        var book = JsonSerializer.Deserialize(json, AppJsonContext.Default.BookSummary)!;
        Assert.Equal(5_000_000_000L, book.FileSize);
    }

    [Fact]
    public void BookDetailFileSizeSurvivesBeyondIntRange()
    {
        const string json = """{"id": "b1", "file_size": 5000000000}""";
        var book = JsonSerializer.Deserialize(json, AppJsonContext.Default.BookDetail)!;
        Assert.Equal(5_000_000_000L, book.FileSize);
    }

    // is_explicit, is_missing and ocr_indexed are bool()-coerced upstream and
    // never arrive null; indexed, index_failed and has_thumbnail pass through
    // raw nullable columns and can. A null on the coerced trio would throw.
    [Fact]
    public void BookSummaryCoercedBooleansAreNonNullable()
    {
        const string json = """
        {"id": "b1", "is_explicit": true, "is_missing": true, "ocr_indexed": true,
         "indexed": null, "index_failed": null, "has_thumbnail": null}
        """;
        var book = JsonSerializer.Deserialize(json, AppJsonContext.Default.BookSummary)!;
        Assert.True(book.IsExplicit);
        Assert.True(book.IsMissing);
        Assert.True(book.OcrIndexed);
        Assert.Null(book.Indexed);
        Assert.Null(book.IndexFailed);
        Assert.Null(book.HasThumbnail);
    }

    [Fact]
    public void BookDetailCoercedBooleansAreNonNullable()
    {
        const string json = """
        {"id": "b1", "is_explicit": true, "is_missing": true, "ocr_indexed": true,
         "ocr_pending": true, "indexed": null, "index_failed": null, "has_thumbnail": null}
        """;
        var book = JsonSerializer.Deserialize(json, AppJsonContext.Default.BookDetail)!;
        Assert.True(book.IsExplicit);
        Assert.True(book.IsMissing);
        Assert.True(book.OcrIndexed);
        Assert.True(book.OcrPending);
        Assert.Null(book.Indexed);
        Assert.Null(book.IndexFailed);
        Assert.Null(book.HasThumbnail);
    }
}
