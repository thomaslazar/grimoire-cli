using GrimoireCli.Configuration;

namespace GrimoireCli.Tests.Configuration;

public class ConfigLocationTests
{
    private static readonly string Home = Path.Combine(Path.GetTempPath(), "home");
    private static readonly string BinDir = Path.Combine(Path.GetTempPath(), "proj", "bin");
    private static readonly string Exe = Path.Combine(BinDir, "grimoire-cli");
    private static readonly string Sibling = Path.Combine(BinDir, "grimoire-cli.json");
    private static readonly string HomeDefault = Path.Combine(Home, ".grimoire-cli", "config.json");

    private static Func<string, string?> Env(string? value) =>
        name => name == "GRIMOIRE_CONFIG" ? value : null;

    private static Func<string, bool> Exists(params string[] paths) => path => paths.Contains(path);

    [Fact]
    public void TheEnvironmentVariableWins()
    {
        var target = Path.Combine(Path.GetTempPath(), "gm.json");
        var location = ConfigManager.Locate(Env(target), Exe, Exists(Sibling), Home);
        Assert.Equal(new ConfigLocation(target, "env"), location);
    }

    // The override names the file to use, so whether it exists yet does not
    // matter: login is what creates it.
    [Fact]
    public void TheEnvironmentVariableIsHonouredBeforeTheFileExists()
    {
        var target = Path.Combine(Path.GetTempPath(), "not-yet.json");
        var location = ConfigManager.Locate(Env(target), Exe, Exists(), Home);
        Assert.Equal(new ConfigLocation(target, "env"), location);
    }

    [Fact]
    public void ARelativeEnvironmentPathIsMadeAbsolute()
    {
        var location = ConfigManager.Locate(Env("gm.json"), Exe, Exists(), Home);
        Assert.Equal(Path.GetFullPath("gm.json"), location.Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyEnvironmentVariableIsIgnored(string value)
    {
        var location = ConfigManager.Locate(Env(value), Exe, Exists(), Home);
        Assert.Equal(new ConfigLocation(HomeDefault, "home"), location);
    }

    [Fact]
    public void AFileBesideTheBinaryIsUsedWhenPresent()
    {
        var location = ConfigManager.Locate(Env(null), Exe, Exists(Sibling), Home);
        Assert.Equal(new ConfigLocation(Sibling, "binary"), location);
    }

    // An absent sibling must not claim the path, or every install would move
    // its config beside itself on first login.
    [Fact]
    public void AnAbsentFileBesideTheBinaryFallsThroughToHome()
    {
        var location = ConfigManager.Locate(Env(null), Exe, Exists(), Home);
        Assert.Equal(new ConfigLocation(HomeDefault, "home"), location);
    }

    [Fact]
    public void AnUnknownExecutablePathFallsThroughToHome()
    {
        var location = ConfigManager.Locate(Env(null), null, Exists(Sibling), Home);
        Assert.Equal(new ConfigLocation(HomeDefault, "home"), location);
    }
}
