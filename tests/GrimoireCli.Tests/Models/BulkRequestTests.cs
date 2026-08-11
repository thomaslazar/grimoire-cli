using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class BulkRequestTests
{
    private static GameSystemBulkUpdateRequest ParseUpdate(string json)
        => JsonSerializer.Deserialize(json, AppJsonContext.Default.GameSystemBulkUpdateRequest)!;

    private static BulkAddTagsRequest ParseTags(string json)
        => JsonSerializer.Deserialize(json, AppJsonContext.Default.BulkAddTagsRequest)!;

    [Fact]
    public void AcceptsItemsCarryingAnIdAndFields()
    {
        var req = ParseUpdate("""{"items":[{"id":"a","year":2009},{"id":"b","genres":["Fantasy"]}]}""");
        Assert.Equal(2, req.Items.Count);
        Assert.Equal("a", req.Items[0].Id);
        Assert.Equal(2009, req.Items[0].Year);
    }

    [Fact]
    public void RequiresItems()
    {
        Assert.Throws<JsonException>(() => ParseUpdate("{}"));
    }

    // The batch item's id is mandatory where the single-item body must not carry
    // one at all — which is why they are separate types.
    [Fact]
    public void RequiresAnIdOnEveryItem()
    {
        Assert.Throws<JsonException>(() => ParseUpdate("""{"items":[{"year":2009}]}"""));
    }

    // Disallow does not inherit, so the derived item type repeats the attribute.
    [Fact]
    public void RejectsAMisspelledFieldInsideAnItem()
    {
        var ex = Assert.Throws<JsonException>(() => ParseUpdate("""{"items":[{"id":"a","yaer":1}]}"""));
        Assert.Equal("$.items[0].yaer", ex.Path);
    }

    [Fact]
    public void RejectsAnUnknownEnvelopeKey()
    {
        var ex = Assert.Throws<JsonException>(() => ParseUpdate("""{"itesm":[]}"""));
        Assert.Equal("$.itesm", ex.Path);
    }

    [Fact]
    public void AcceptsIdsAndTags()
    {
        var req = ParseTags("""{"ids":["a","b"],"tags":["cyberpunk"]}""");
        Assert.Equal(2, req.Ids.Count);
        Assert.Single(req.Tags);
    }

    [Theory]
    [InlineData("""{"ids":["a"]}""")]
    [InlineData("""{"tags":["t"]}""")]
    public void RequiresBothIdsAndTags(string json)
    {
        Assert.Throws<JsonException>(() => ParseTags(json));
    }

    [Fact]
    public void RejectsAnUnknownTagEnvelopeKey()
    {
        Assert.Throws<JsonException>(() => ParseTags("""{"ids":["a"],"tags":["t"],"remove":["x"]}"""));
    }
}
