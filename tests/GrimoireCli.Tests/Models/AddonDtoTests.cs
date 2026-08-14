using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class AddonDtoTests
{
    // Captured from the running stack: GET /api/addons after installing one
    // add-on from the community index.
    [Fact]
    public void AddonListResponseSplitsInstalledFromAvailable()
    {
        const string json = """
        {"installed": [{"id": "ttrpg-wiki", "name": "TTRPG Wiki", "version": "1.0.1",
          "kind": "scraper", "target": "game-system", "requires_script": false,
          "script_approved": false, "enabled": true, "runnable": true,
          "blocked_reason": "", "source": "index", "available_version": "1.0.1",
          "update_available": false}],
         "available": [{"id": "ttrpg-wiki", "name": "TTRPG Wiki", "kind": "scraper",
          "target": "game-system", "version": "1.0.1", "requires_script": false,
          "script_sha256": "abc", "installed": true, "update_available": false}],
         "index_url": "https://example.test/index.json",
         "default_index_url": "https://example.test/index.json",
         "allow_scripts": false, "index_generated": "2026-08-12T03:19:48Z"}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.AddonListResponse)!;
        var installed = Assert.Single(result.Installed!);
        Assert.True(installed.Enabled);
        Assert.True(installed.Runnable);
        Assert.Equal("game-system", installed.Target);
        var available = Assert.Single(result.Available!);
        Assert.Equal("abc", available.ScriptSha256);
        Assert.True(available.Installed);
        Assert.False(result.AllowScripts);
    }

    // An installed-but-blocked add-on is the state that explains an empty
    // metadata-sources list, so the two fields carrying it must survive.
    [Fact]
    public void AddonInstalledCarriesTheBlockedState()
    {
        const string json = """
        {"id": "x", "name": "X", "enabled": true, "runnable": false,
         "blocked_reason": "script not approved", "requires_script": true,
         "script_approved": false, "update_available": false}
        """;
        var addon = JsonSerializer.Deserialize(json, AppJsonContext.Default.AddonInstalled)!;
        Assert.True(addon.Enabled);
        Assert.False(addon.Runnable);
        Assert.Equal("script not approved", addon.BlockedReason);
    }

    [Fact]
    public void UpgradeAllResultReadsBothLists()
    {
        const string json = """
        {"status": "ok",
         "updated": [{"id": "a", "from": "1.0.0", "to": "1.1.0"}],
         "failed": [{"id": "b", "error": "could not reach source"}]}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.UpgradeAllResult)!;
        var upgraded = Assert.Single(result.Updated!);
        Assert.Equal("1.0.0", upgraded.From);
        Assert.Equal("1.1.0", upgraded.To);
        var failure = Assert.Single(result.Failed!);
        Assert.Equal("b", failure.Id);
        Assert.Equal("could not reach source", failure.Error);
    }

    [Fact]
    public void RefreshResultAndSettingsRoundTrip()
    {
        var refresh = JsonSerializer.Deserialize("""{"status": "ok", "count": 2}""",
            AppJsonContext.Default.RefreshResult)!;
        Assert.Equal(2, refresh.Count);
        var settings = JsonSerializer.Deserialize(
            """{"index_url": "https://example.test/index.json", "allow_scripts": true}""",
            AppJsonContext.Default.AddonSettings)!;
        Assert.True(settings.AllowScripts);
        Assert.Equal("https://example.test/index.json", settings.IndexUrl);
    }
}
