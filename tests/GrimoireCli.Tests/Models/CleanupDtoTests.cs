using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class CleanupDtoTests
{
    // Shape from routers/maintenance/_helpers.py:113 — five fixed keys, one per
    // resource the sweep covers.
    [Fact]
    public void CleanupResultCarriesEveryCount()
    {
        const string json = """
        {"removed": {"books": 41, "maps": 2, "tokens": 0, "audio": 1, "systems": 3}}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.CleanupResult)!;
        Assert.Equal(41, result.Removed!.Books);
        Assert.Equal(2, result.Removed.Maps);
        Assert.Equal(0, result.Removed.Tokens);
        Assert.Equal(1, result.Removed.Audio);
        // Nothing in the request names a system: systems are pruned as a
        // consequence of the book sweep, so this is the count a caller is least
        // likely to expect and the one most worth pinning.
        Assert.Equal(3, result.Removed.Systems);
    }

    [Fact]
    public void AllZeroIsTheHealthyResponse()
    {
        const string json = """
        {"removed": {"books": 0, "maps": 0, "tokens": 0, "audio": 0, "systems": 0}}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.CleanupResult)!;
        Assert.Equal(0, result.Removed!.Books);
        Assert.Equal(0, result.Removed.Systems);
    }
}
