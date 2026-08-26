using GrimoireCli.Api;
using GrimoireCli.Configuration;

namespace GrimoireCli.Commands;

public static class CommandHelper
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static (GrimoireApiClient client, AppConfig config) BuildClient(
        string? serverOverride = null)
    {
        var configManager = new ConfigManager();
        var config = configManager.Resolve(flagServer: serverOverride);

        if (string.IsNullOrEmpty(config.Server))
        {
            _logger.Error("No server configured. Run: grimoire-cli login");
            Environment.Exit(1);
        }

        if (string.IsNullOrEmpty(config.AccessToken))
        {
            _logger.Error("Not authenticated. Run: grimoire-cli login");
            Environment.Exit(1);
        }

        return (new GrimoireApiClient(config, configManager), config);
    }

    public static string ReadJsonInput(string input)
    {
        if (File.Exists(input))
            return File.ReadAllText(input);
        return input;
    }
}
