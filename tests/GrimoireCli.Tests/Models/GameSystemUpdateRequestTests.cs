using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

/// <summary>
/// Grimoire drops unknown keys at pydantic validation and answers {"status":"ok"},
/// so a misspelled field silently changes nothing. These types are what turns that
/// silent no-op into a client-side refusal.
/// </summary>
public class GameSystemUpdateRequestTests
{
    private static GameSystemUpdateRequest Parse(string json)
        => JsonSerializer.Deserialize(json, AppJsonContext.Default.GameSystemUpdateRequest)!;

    [Fact]
    public void AcceptsEveryEditableField()
    {
        const string json = """
        {
          "name": "Shadowrun 4 DE",
          "description": "d",
          "publishers": [{"name": "Pegasus Spiele", "url": ""}],
          "character_builder_url": "https://old",
          "character_builder_urls": [{"label": "Chummer", "url": "https://c"}],
          "urls": [{"label": "Site", "url": "https://s"}],
          "tags": ["cyberpunk"],
          "genre": "Cyberpunk",
          "genres": ["Cyberpunk"],
          "dice_materials": ["d6"],
          "system_family": "Shadowrun",
          "parent_system": "Shadowrun",
          "edition": "4 DE",
          "license": "Proprietary",
          "year": 2009,
          "cover_book_id": "abc",
          "is_explicit": false
        }
        """;
        var req = Parse(json);
        Assert.Equal("Shadowrun 4 DE", req.Name);
        Assert.Equal(2009, req.Year);
        Assert.Equal("Pegasus Spiele", req.Publishers![0].Name);
        Assert.Equal("Chummer", req.CharacterBuilderUrls![0].Label);
        Assert.False(req.IsExplicit);
    }

    [Fact]
    public void RejectsAMisspelledField()
    {
        var ex = Assert.Throws<JsonException>(() => Parse("""{"nmae":"typo"}"""));
        Assert.Equal("$.nmae", ex.Path);
    }

    // id is not editable, so the same check that catches typos catches a body
    // pasted from a systems get dump or a batch-update file.
    [Fact]
    public void RejectsId()
    {
        var ex = Assert.Throws<JsonException>(() => Parse("""{"id":"abc","name":"x"}"""));
        Assert.Equal("$.id", ex.Path);
    }

    // Read-only fields from the 31-field response DTO must not be waved through.
    [Theory]
    [InlineData("book_count")]
    [InlineData("has_cover")]
    [InlineData("child_count")]
    [InlineData("name_is_custom")]
    public void RejectsAReadOnlyResponseField(string field)
    {
        Assert.Throws<JsonException>(() => Parse($"{{\"{field}\": 1}}"));
    }

    [Fact]
    public void RejectsAWrongType()
    {
        var ex = Assert.Throws<JsonException>(() => Parse("""{"year":"soon"}"""));
        Assert.Equal("$.year", ex.Path);
    }

    // Disallow does not propagate into element types, so the nested entry types
    // carry it themselves.
    [Fact]
    public void RejectsAMisspelledFieldInsideAPublisher()
    {
        var ex = Assert.Throws<JsonException>(
            () => Parse("""{"publishers":[{"nmae":"typo"}]}"""));
        Assert.Equal("$.publishers[0].nmae", ex.Path);
    }

    [Fact]
    public void RejectsAMisspelledFieldInsideALinkEntry()
    {
        var ex = Assert.Throws<JsonException>(
            () => Parse("""{"urls":[{"lable":"typo"}]}"""));
        Assert.Equal("$.urls[0].lable", ex.Path);
    }

    // Value rules stay the server's: "" is how a field is cleared, and a blank
    // name is a 422 from Grimoire, not a client-side refusal.
    [Fact]
    public void AcceptsEmptyStringsAndABlankName()
    {
        Assert.Equal("", Parse("""{"system_family":""}""").SystemFamily);
        Assert.Equal("", Parse("""{"name":""}""").Name);
    }

    [Fact]
    public void AcceptsAnEmptyBody()
    {
        Assert.Null(Parse("{}").Name);
    }
}
