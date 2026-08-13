using GrimoireCli.Configuration;

namespace GrimoireCli.Tests.Configuration;

public class ConfigManagerTests
{
    private static ConfigManager InTempDir(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "config.json");
        return new ConfigManager(path);
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var manager = InTempDir(out var path);
        try
        {
            manager.Save(new AppConfig { Server = "https://example.invalid", AccessToken = "tok" });
            var loaded = manager.Load();
            Assert.Equal("https://example.invalid", loaded.Server);
            Assert.Equal("tok", loaded.AccessToken);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void LoadReturnsEmptyConfigWhenFileIsAbsent()
    {
        var manager = new ConfigManager(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "config.json"));
        var loaded = manager.Load();
        Assert.Null(loaded.Server);
        Assert.Null(loaded.AccessToken);
    }

    [Fact]
    public void ResolvePrefersFlagOverEnvAndFile()
    {
        var manager = InTempDir(out var path);
        try
        {
            manager.Save(new AppConfig { Server = "https://file.invalid", AccessToken = "file-token" });
            var resolved = manager.Resolve(
                flagServer: "https://flag.invalid",
                flagToken: "flag-token",
                envLookup: key => key == "GRIMOIRE_SERVER" ? "https://env.invalid" : "env-token");
            Assert.Equal("https://flag.invalid", resolved.Server);
            Assert.Equal("flag-token", resolved.AccessToken);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void ResolvePrefersEnvOverFile()
    {
        var manager = InTempDir(out var path);
        try
        {
            manager.Save(new AppConfig { Server = "https://file.invalid", AccessToken = "file-token" });
            var resolved = manager.Resolve(
                envLookup: key => key == "GRIMOIRE_SERVER" ? "https://env.invalid" : "env-token");
            Assert.Equal("https://env.invalid", resolved.Server);
            Assert.Equal("env-token", resolved.AccessToken);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void ResolveFallsBackToTheFile()
    {
        var manager = InTempDir(out var path);
        try
        {
            manager.Save(new AppConfig { Server = "https://file.invalid", AccessToken = "file-token" });
            var resolved = manager.Resolve(envLookup: _ => null);
            Assert.Equal("https://file.invalid", resolved.Server);
            Assert.Equal("file-token", resolved.AccessToken);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    // Resolve must carry these through. If it drops them, LastVersionCheck arrives
    // null on every run and the CLI probes on every single invocation — the whole
    // point of the cadence, silently defeated, with no other test failing.
    [Fact]
    public void ResolveCarriesTheVersionCheckState()
    {
        var manager = InTempDir(out var path);
        try
        {
            var checkedAt = new DateTimeOffset(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);
            manager.Save(new AppConfig
            {
                Server = "http://example.test",
                LastServerVersion = "1.5.6",
                LastVersionCheck = checkedAt,
            });
            var resolved = manager.Resolve(envLookup: _ => null);
            Assert.Equal("1.5.6", resolved.LastServerVersion);
            Assert.Equal(checkedAt, resolved.LastVersionCheck);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void UpdateVersionCheckPreservesUnrelatedFields()
    {
        var manager = InTempDir(out var path);
        try
        {
            manager.Save(new AppConfig { Server = "http://example.test", AccessToken = "on-disk-token" });
            var checkedAt = new DateTimeOffset(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);
            manager.UpdateVersionCheck("1.5.6", checkedAt);
            var reloaded = manager.Load();
            Assert.Equal("http://example.test", reloaded.Server);
            Assert.Equal("on-disk-token", reloaded.AccessToken);
            Assert.Equal("1.5.6", reloaded.LastServerVersion);
            Assert.Equal(checkedAt, reloaded.LastVersionCheck);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    // The hazard this design exists to avoid: Resolve merges env vars into memory,
    // so persisting the resolved config would write a token the operator kept out
    // of the file. UpdateVersionCheck reads the file, not the resolved config.
    [Fact]
    public void UpdateVersionCheckDoesNotPersistEnvironmentValues()
    {
        var manager = InTempDir(out var path);
        try
        {
            manager.Save(new AppConfig { Server = "http://example.test" });
            manager.Resolve(envLookup: name => name == "GRIMOIRE_TOKEN" ? "env-only-token" : null);
            manager.UpdateVersionCheck("1.5.6", DateTimeOffset.UtcNow);
            var raw = File.ReadAllText(path);
            Assert.DoesNotContain("env-only-token", raw);
            Assert.Null(manager.Load().AccessToken);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    // A first check on a machine with no config file creates one holding only these
    // two fields. Accepted deliberately: no secrets, and it stops a re-probe on
    // every invocation.
    [Fact]
    public void UpdateVersionCheckCreatesAConfigWhenNoneExists()
    {
        var manager = InTempDir(out var path);
        try
        {
            manager.UpdateVersionCheck("1.5.6", DateTimeOffset.UtcNow);
            var written = new ConfigManager(path).Load();
            Assert.Equal("1.5.6", written.LastServerVersion);
            Assert.NotNull(written.LastVersionCheck);
            Assert.Null(written.Server);
            Assert.Null(written.AccessToken);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
