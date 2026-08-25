using GrimoireCli.Configuration;

namespace GrimoireCli.Tests.Configuration;

// Load warns when a config is unparseable, and NLog's configuration is
// process-global, so these must not run beside a test asserting on log contents.
[Collection("NLog")]
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

    // A config file that is not valid JSON used to kill every command with a raw
    // JsonException and exit 1 — including the login that would repair it.
    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("{\"server\": \"http://x\"")]
    [InlineData("[]")]
    public void LoadTreatsAnUnparseableFileAsAbsent(string content)
    {
        var manager = InTempDir(out var path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        try
        {
            var config = manager.Load();
            Assert.Null(config.Server);
            Assert.Null(config.AccessToken);
            // The bytes are preserved beside the config: a hand-edit that broke a
            // 30-day non-refreshable token must be recoverable.
            Assert.Equal(content, File.ReadAllText($"{path}.corrupt"));
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    // Recovery has to work without the operator deleting anything by hand.
    [Fact]
    public void SaveOverwritesAnUnparseableFile()
    {
        var manager = InTempDir(out var path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{not json");
        try
        {
            manager.Save(new AppConfig { Server = "http://example.test", AccessToken = "t" });
            Assert.Equal("http://example.test", manager.Load().Server);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    // The write goes to a temporary file and is renamed over the target, so a reader
    // never sees a partial file. The temporary must not survive the write.
    [Fact]
    public void SaveLeavesNoTemporaryFileBehind()
    {
        var manager = InTempDir(out var path);
        try
        {
            manager.Save(new AppConfig { Server = "http://example.test" });
            var dir = Path.GetDirectoryName(path)!;
            Assert.Equal(["config.json"], Directory.GetFiles(dir).Select(Path.GetFileName).Order());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    // The write must replace the file rather than rewrite it in place, which is what
    // keeps a torn write from destroying the token already there. A hard link is how
    // that becomes observable: an in-place write updates every link, while replacing
    // the path leaves the link holding the old bytes. This test fails if Save goes
    // back to File.WriteAllText.
    [Fact]
    public void SaveReplacesTheFileRatherThanRewritingItInPlace()
    {
        var manager = InTempDir(out var path);
        var link = Path.Combine(Path.GetDirectoryName(path)!, "link.json");
        try
        {
            manager.Save(new AppConfig { Server = "http://first.test", AccessToken = "first" });
            var before = File.ReadAllText(path);
            File.CreateSymbolicLink(link, path);
            using (var pin = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                // A reader holding the old file open must keep seeing the old bytes.
                manager.Save(new AppConfig { Server = "http://second.test", AccessToken = "second" });
                Assert.Equal(before, new StreamReader(pin).ReadToEnd());
            }
            Assert.Equal("http://second.test", manager.Load().Server);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    // The file holds a bearer token valid for 30 days, so it must not be left at
    // whatever the umask allows — and replacing the path swaps in the new file's
    // mode, which would silently undo an operator's chmod on every write.
    [Fact]
    public void SaveRestrictsThePermissionsToTheOwner()
    {
        if (OperatingSystem.IsWindows()) return;
        var manager = InTempDir(out var path);
        try
        {
            manager.Save(new AppConfig { AccessToken = "a-token" });
            var mode = File.GetUnixFileMode(path);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
            manager.Save(new AppConfig { AccessToken = "another" });
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void SaveReportsAnUnwritableConfigInsteadOfThrowingRaw()
    {
        if (OperatingSystem.IsWindows()) return;
        var manager = InTempDir(out var path);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        // A directory the process cannot write is the realistic case: a read-only
        // home, a full disk, a mount gone away.
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var ex = Assert.Throws<ConfigWriteException>(() => manager.Save(new AppConfig()));
            Assert.Contains(path, ex.Message);
        }
        finally
        {
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(dir, recursive: true);
        }
    }

    // A token from a flag or the environment belongs to a different session than
    // the stored cookie, so the cookie must not be offered as a way to renew it.
    [Fact]
    public void ResolveDropsRefreshTokenWhenTheAccessTokenIsOverridden()
    {
        var manager = InTempDir(out var path);
        try
        {
            manager.Save(new AppConfig
            {
                Server = "http://example.test",
                AccessToken = "file-access",
                RefreshToken = "file-refresh"
            });
            var fromFile = manager.Resolve();
            Assert.Equal("file-refresh", fromFile.RefreshToken);
            var flagged = manager.Resolve(flagToken: "flag-access");
            Assert.Null(flagged.RefreshToken);
            var fromEnv = manager.Resolve(
                envLookup: name => name == "GRIMOIRE_TOKEN" ? "env-access" : null);
            Assert.Null(fromEnv.RefreshToken);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void UpdateTokensWritesBothAndDoesNotPersistEnvironmentValues()
    {
        var manager = InTempDir(out var path);
        try
        {
            manager.Save(new AppConfig { Server = "http://example.test" });
            manager.Resolve(envLookup: name => name == "GRIMOIRE_TOKEN" ? "env-only-token" : null);
            manager.UpdateTokens("new-access", "new-refresh");
            var raw = File.ReadAllText(path);
            Assert.DoesNotContain("env-only-token", raw);
            var back = manager.Load();
            Assert.Equal("new-access", back.AccessToken);
            Assert.Equal("new-refresh", back.RefreshToken);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    // The server rotates on every refresh, so a 200 that carried no new cookie
    // leaves the stored one as the best available credential.
    [Fact]
    public void UpdateTokensKeepsTheStoredRefreshTokenWhenGivenNull()
    {
        var manager = InTempDir(out var path);
        try
        {
            manager.Save(new AppConfig { AccessToken = "old", RefreshToken = "keep-me" });
            manager.UpdateTokens("new-access", null);
            var back = manager.Load();
            Assert.Equal("new-access", back.AccessToken);
            Assert.Equal("keep-me", back.RefreshToken);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
