using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class MetadataDtoTests
{
    // Shape from routers/_metadata_lookup.py:list_sources. supports_paste is
    // what tells a caller whether metadata-fetch --paste is available.
    [Fact]
    public void SourceListCarriesSupportsPaste()
    {
        const string json = """
        {"sources": [{"id": "fixture-source", "name": "Fixture Source",
          "description": "Local fixture.", "homepage": "", "attribution": "",
          "supports_paste": true}]}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.MetadataSourceList)!;
        var source = Assert.Single(result.Sources!);
        Assert.Equal("fixture-source", source.Id);
        Assert.True(source.SupportsPaste);
    }

    // Shape from addons/interpreter.py:search. query echoes the effective
    // query, which is the resource's own name when the caller sent none.
    [Fact]
    public void SearchResultEchoesTheEffectiveQuery()
    {
        const string json = """
        {"query": "Shadowrun 4 DE",
         "results": [{"identity": "shadowrun-4-de", "label": "Shadowrun 4 DE",
           "score": 1.0, "url": "https://fixture.test/systems/shadowrun-4-de"}]}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.MetadataSearchResult)!;
        Assert.Equal("Shadowrun 4 DE", result.Query);
        var candidate = Assert.Single(result.Results!);
        Assert.Equal("shadowrun-4-de", candidate.Identity);
        Assert.Equal(1.0, candidate.Score);
    }

    // current and incoming are typed per field by the server: a string, an int,
    // a list of strings, or a list of objects. JsonElement is what lets one DTO
    // carry all four, and re-emit each verbatim.
    [Fact]
    public void FieldDiffCarriesEveryValueShape()
    {
        const string json = """
        {"source_id": "fixture-source", "identity": "shadowrun-4-de",
         "url": "https://fixture.test/systems/shadowrun-4-de",
         "attribution": "Fixture data",
         "fields": [
           {"field": "system_family", "current": null, "incoming": "Shadowrun",
            "status": "only_incoming"},
           {"field": "year", "current": 2005, "incoming": 2006, "status": "differs"},
           {"field": "genres", "current": ["Cyberpunk"],
            "incoming": ["Cyberpunk", "Urban Fantasy"], "status": "differs"},
           {"field": "urls", "current": [{"label": "Wiki", "url": "https://a"}],
            "incoming": [{"label": "Wiki", "url": "https://a"},
                         {"label": "Source", "url": "https://b"}],
            "status": "only_incoming"}]}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.MetadataFetchResult)!;
        Assert.Equal("fixture-source", result.SourceId);
        Assert.Equal(4, result.Fields!.Count);
        Assert.Equal("only_incoming", result.Fields[0].Status);
        Assert.Null(result.Fields[0].Current);
        Assert.Equal("Shadowrun", result.Fields[0].Incoming!.Value.GetString());
        Assert.Equal(2005, result.Fields[1].Current!.Value.GetInt32());
        Assert.Equal(JsonValueKind.Array, result.Fields[2].Incoming!.Value.ValueKind);
        Assert.Equal("Source",
            result.Fields[3].Incoming!.Value[1].GetProperty("label").GetString());
    }

    // Round-tripping is what proves stdout stays the server's own JSON: the
    // CLI writes these DTOs back out, and a JsonElement must survive that.
    [Fact]
    public void FieldDiffReEmitsItsValuesVerbatim()
    {
        const string json = """
        {"field": "publishers",
         "current": null,
         "incoming": [{"name": "FanPro", "url": "https://fanpro.test"}],
         "status": "only_incoming"}
        """;
        var diff = JsonSerializer.Deserialize(json, AppJsonContext.Default.MetadataFieldDiff)!;
        var written = JsonSerializer.Serialize(diff, AppJsonContext.Default.MetadataFieldDiff);
        using var reparsed = JsonDocument.Parse(written);
        var incoming = reparsed.RootElement.GetProperty("incoming");
        Assert.Equal("FanPro", incoming[0].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, reparsed.RootElement.GetProperty("current").ValueKind);
    }
}
