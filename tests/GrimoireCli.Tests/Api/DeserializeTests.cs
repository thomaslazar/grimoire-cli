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
}
