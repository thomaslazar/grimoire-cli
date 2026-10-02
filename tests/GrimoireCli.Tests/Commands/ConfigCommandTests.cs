using GrimoireCli.Commands;
using GrimoireCli.Configuration;

namespace GrimoireCli.Tests.Commands;

public class ConfigCommandTests
{
    // "Why is this the wrong account?" is the question per-install config
    // creates, so the command that answers it must teach the order.
    [Fact]
    public void GetTeachesTheResolutionOrder()
    {
        var output = HelpRenderer.Render(ConfigCommand.Create(), ["config", "get"], false);
        Assert.Contains("GRIMOIRE_CONFIG", output);
        Assert.Contains("grimoire-cli.json beside the binary, if it exists", output);
        Assert.Contains("~/.grimoire-cli/config.json", output);
    }

    [Fact]
    public void GetTeachesHowToBootstrapAnInstallsOwnAccount()
    {
        var output = HelpRenderer.Render(ConfigCommand.Create(), ["config", "get"], false);
        Assert.Contains("echo '{}' > bin/grimoire-cli.json", output);
    }

    [Fact]
    public void GetWarnsThatASymlinkResolvesToTheRealFile()
    {
        var output = HelpRenderer.Render(ConfigCommand.Create(), ["config", "get"], false);
        Assert.Contains("symlinked binary", output);
    }

    [Fact]
    public void LoginSaysWhichFileItWrites()
    {
        var output = HelpRenderer.Render(LoginCommand.Create(), ["login"], false);
        Assert.Contains("Writes the resolved config file; config get reports which one.", output);
    }

    [Fact]
    public void GetMasksTheApiKeyAndReportsKeyAuth()
    {
        var display = ConfigCommand.Display(
            new AppConfig { ApiKey = "grim_x", AccessToken = "stale" }, new ConfigLocation("/c.json", "default"));
        Assert.Equal("***", display["apiKey"]);
        Assert.Equal("api-key", display["auth"]);
    }

    [Theory]
    [InlineData("t", "session")]
    [InlineData(null, "(none)")]
    public void GetReportsSessionOrNoAuth(string? accessToken, string expected)
    {
        var display = ConfigCommand.Display(
            new AppConfig { AccessToken = accessToken }, new ConfigLocation("/c.json", "default"));
        Assert.Equal("(not set)", display["apiKey"]);
        Assert.Equal(expected, display["auth"]);
    }

    [Fact]
    public void GetExplainsTheAuthField()
    {
        var output = HelpRenderer.Render(ConfigCommand.Create(), ["config", "get"], false);
        Assert.Contains("auth is api-key or session; login sets one and clears the other.", output);
    }
}
