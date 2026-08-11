using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class BulkResultTests
{
    [Fact]
    public void ReadsUpdatedAndErrors()
    {
        const string json = """
        {"updated":["a"],"errors":[{"id":"bogus","detail":"System not found"}]}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.BulkUpdateResult)!;
        Assert.Equal(["a"], result.Updated);
        Assert.Equal("bogus", result.Errors![0].Id);
        Assert.Equal("System not found", result.Errors[0].Detail);
    }

    [Fact]
    public void ReadsTheTagMapOnTheTagResponse()
    {
        const string json = """
        {"updated":["a"],"errors":[],"tags":{"a":["Cyberpunk","Smoke"]}}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.BulkTagResult)!;
        Assert.Empty(result.Errors!);
        Assert.Equal(["Cyberpunk", "Smoke"], result.Tags!["a"]);
    }
}
