using System.Text.Json;
using System.Text.Json.Serialization;
using GrimoireCli.Commands;
using Microsoft.Kiota.Abstractions.Serialization;

namespace GrimoireCli.Tests.Commands;

public class JsonExamplesTests
{
    [Fact]
    public void EverySampleParsesAsJson()
    {
        Assert.NotEmpty(JsonExamples.All);
        foreach (var (type, sample) in JsonExamples.All)
        {
            var ex = Record.Exception(() => JsonDocument.Parse(sample));
            Assert.True(ex is null, $"Sample for {type.Name} is not valid JSON: {ex?.Message}\n{sample}");
        }
    }

    // The root keys of every sample are exactly the wire fields the model
    // deserializes — the same set JsonBodyInput.Validate accepts. If these ever
    // diverge, --help-full advertises a body the CLI itself refuses.
    // SavedFile is skipped: it is the CLI's own --output receipt, a plain POCO
    // with no GetFieldDeserializers() to compare against.
    [Fact]
    public void EverySampleRootMatchesTheModelsWireFields()
    {
        foreach (var (type, sample) in JsonExamples.All)
        {
            if (type == typeof(GrimoireCli.Models.SavedFile)) continue;
            var model = (IParsable)Activator.CreateInstance(type)!;
            var expected = model.GetFieldDeserializers().Keys.OrderBy(k => k, StringComparer.Ordinal);
            var actual = JsonDocument.Parse(sample).RootElement
                .EnumerateObject().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal);
            Assert.Equal(expected, actual);
        }
    }

    // Discovery covers the whole Models tree, so a predicate that quietly stopped
    // matching would leave a nearly empty file that the tests above still pass.
    [Fact]
    public void CoversTheWholeModelTree()
    {
        Assert.True(JsonExamples.All.Count > 80, $"Only {JsonExamples.All.Count} models discovered.");
    }

    [Fact]
    public void ScalarsRenderWithTheirType()
    {
        var sample = JsonDocument.Parse(
            JsonExamples.For(typeof(GrimoireCli.Generated.Models.GameSystemUpdate))).RootElement;
        Assert.Equal(JsonValueKind.String, sample.GetProperty("name").ValueKind);
        Assert.Equal(0, sample.GetProperty("year").GetInt32());
        Assert.Equal(JsonValueKind.False, sample.GetProperty("is_explicit").ValueKind);
    }

    [Fact]
    public void ListsOfStringsRenderAsAOneElementArray()
    {
        var sample = JsonDocument.Parse(
            JsonExamples.For(typeof(GrimoireCli.Generated.Models.GameSystemUpdate))).RootElement;
        var tags = sample.GetProperty("tags");
        Assert.Equal(JsonValueKind.Array, tags.ValueKind);
        Assert.Equal("<string>", Assert.Single(tags.EnumerateArray()).GetString());
    }

    // RescanRequest.MetadataMode is the tree's only true C# enum; the
    // placeholder must join every member's [EnumMember] wire string ("new",
    // "missing", "replace"), not a CLR member name a ToString() fallback
    // would emit, and not just one of the three as a real value.
    [Fact]
    public void EnumsRenderTheirWireValuesAsAPlaceholder()
    {
        var sample = JsonDocument.Parse(
            JsonExamples.For(typeof(GrimoireCli.Generated.Models.RescanRequest))).RootElement;
        Assert.Equal("<new|missing|replace>", sample.GetProperty("metadata_mode").GetString());
    }

    [Fact]
    public void NestedModelsRenderTheirOwnFields()
    {
        var sample = JsonDocument.Parse(
            JsonExamples.For(typeof(GrimoireCli.Generated.Models.GameSystemUpdate))).RootElement;
        var publisher = Assert.Single(sample.GetProperty("publishers").EnumerateArray());
        Assert.Equal("<string>", publisher.GetProperty("name").GetString());
        Assert.Equal("<string>", publisher.GetProperty("url").GetString());
    }

    // The bulk endpoints take an envelope, and the envelope is the model
    // JsonBodyInput.Validate parses against.
    [Fact]
    public void EnvelopeModelsNestTheirItems()
    {
        var sample = JsonDocument.Parse(
            JsonExamples.For(typeof(GrimoireCli.Generated.Models.GameSystemBulkUpdate))).RootElement;
        var item = Assert.Single(sample.GetProperty("items").EnumerateArray());
        Assert.Equal("<string>", item.GetProperty("id").GetString());
        Assert.Equal("<string>", item.GetProperty("system_family").GetString());
    }

    // SavedFilterUpdate.State is an UntypedNode — a free-form object the spec
    // gives no shape for.
    [Fact]
    public void UntypedNodesRenderAsAnEmptyObject()
    {
        var sample = JsonDocument.Parse(
            JsonExamples.For(typeof(GrimoireCli.Generated.Models.SavedFilterUpdate))).RootElement;
        Assert.Equal(JsonValueKind.Object, sample.GetProperty("state").ValueKind);
        Assert.Empty(sample.GetProperty("state").EnumerateObject());
    }
}
