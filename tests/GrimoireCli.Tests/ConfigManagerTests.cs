using GrimoireCli.Configuration;

namespace GrimoireCli.Tests;

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
}
