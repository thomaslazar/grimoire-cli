using System.Text.Json;
using GrimoireCli.Api;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Api;

public class DeserializeTests
{
    // The success path every typed client overload relies on — exercised here
    // without a server, since it's a pure function of a JSON string.
    [Fact]
    public void DeserializesValidJsonIntoTheRequestedType()
    {
        var result = GrimoireApiClient.Deserialize(
            "{\"id\":\"sr6\",\"name\":\"Shadowrun 6\"}",
            AppJsonContext.Default.GameSystemSummary,
            "api/systems/sr6");

        Assert.Equal("sr6", result.Id);
        Assert.Equal("Shadowrun 6", result.Name);
    }

    // Confirms the premise behind GrimoireApiClient.Deserialize's catch clause:
    // an HTML body — what Grimoire's SPA catch-all returns for an unroutable
    // id — fails as a JsonException, not some other exception type, so the
    // catch actually intercepts it. Deserialize itself isn't called here
    // because its failure path calls Environment.Exit, which would tear down
    // the test process; that path is covered instead by docker/smoke-test.sh.
    [Fact]
    public void HtmlSpaCatchAllBodyFailsWithJsonException()
    {
        const string html = "<!doctype html><html><body>Not Found</body></html>";
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize(html, AppJsonContext.Default.GameSystemSummary));
    }

    // A 200 with a genuinely-JSON-but-truncated body (a server cutting the
    // response short) must also land in the JsonException catch — it is not
    // just an HTML-shaped failure. Same reason Deserialize itself isn't
    // called: its failure path calls Environment.Exit.
    [Fact]
    public void TruncatedJsonBodyFailsWithJsonException()
    {
        const string truncated = "{\"id\":\"sr6\",\"name\":\"Shadowrun 6 DE\",\"book_count\":22";
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize(truncated, AppJsonContext.Default.GameSystemSummary));
    }

    // The one piece of the failure path that IS a pure function reachable
    // without a server or Environment.Exit: the --debug body truncation
    // Deserialize uses so a huge HTML page doesn't flood stderr.
    [Fact]
    public void TruncateForLoggingLeavesShortBodiesUntouched()
    {
        Assert.Equal("short body", GrimoireApiClient.TruncateForLogging("short body"));
    }

    [Fact]
    public void TruncateForLoggingCutsLongBodiesWithLengthAndEllipsis()
    {
        var body = new string('x', 600);
        var result = GrimoireApiClient.TruncateForLogging(body, maxChars: 500);
        Assert.Equal(new string('x', 500) + "... (truncated, 600 chars total)", result);
    }

    [Theory]
    [InlineData("{\"errors\":[{\"id\":\"a\"}]}", "errors", true)]
    [InlineData("{\"errors\":[]}", "errors", false)]
    [InlineData("{\"errors\":null}", "errors", false)]
    [InlineData("{}", "errors", false)]
    [InlineData("{\"errors\":\"not-an-array\"}", "errors", false)]
    [InlineData("not json at all", "errors", false)]
    public void HasItems_ReportsWhetherArrayPropertyIsNonEmpty(string json, string property, bool expected)
        => Assert.Equal(expected, GrimoireApiClient.HasItems(json, property));

    [Theory]
    [InlineData("{\"a\":1}", true)]
    [InlineData("[1,2,3]", true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("<!doctype html><html><body>Not Found</body></html>", false)]
    [InlineData("{\"id\":\"sr6\",\"name\":\"trunc", false)]
    public void IsJsonOrEmpty_AcceptsJsonAndEmptyBodies_RejectsHtmlAndTruncated(string json, bool expected)
        => Assert.Equal(expected, GrimoireApiClient.IsJsonOrEmpty(json));
}
