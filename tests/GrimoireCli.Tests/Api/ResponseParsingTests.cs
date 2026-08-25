using GrimoireCli.Api;

namespace GrimoireCli.Tests.Api;

public class ResponseParsingTests
{
    // The one piece of the failure path that IS a pure function reachable
    // without a server or Environment.Exit: the --debug body truncation
    // response parsing uses so a huge HTML page doesn't flood stderr.
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
