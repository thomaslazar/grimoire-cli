using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Configuration;

public class ConfigManager
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly string _configPath;

    public ConfigManager(string configPath)
    {
        _configPath = configPath;
    }

    public ConfigManager() : this(DefaultConfigPath()) { }

    public static string DefaultConfigPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".grimoire-cli", "config.json");
    }

    /// <summary>
    /// Reads the config file, treating an unreadable or unparseable one as absent.
    /// Every command resolves its config before doing anything, so letting a
    /// JsonException out of here would kill the whole CLI with a stack trace —
    /// including the `login` that would fix it. Warning and continuing keeps the
    /// environment-variable path working and leaves a remedy available; the command
    /// then fails on its own terms ("Not authenticated. Run: grimoire-cli login")
    /// if it needed what the file was holding.
    /// </summary>
    public AppConfig Load()
    {
        if (!File.Exists(_configPath))
            return new AppConfig();

        try
        {
            var json = File.ReadAllText(_configPath);
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.AppConfig) ?? new AppConfig();
        }
        catch (JsonException ex)
        {
            _logger.Warn($"Ignoring {_configPath}: it is not valid JSON ({ex.Message}). "
                         + "Run: grimoire-cli login — that overwrites it.");
            return new AppConfig();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn($"Could not read {_configPath}: {ex.Message}");
            return new AppConfig();
        }
    }

    /// <summary>
    /// Writes the config by creating a temporary file beside it and renaming over
    /// the target, so a reader never sees a half-written file and a crash mid-write
    /// cannot destroy the token already there. The rename is atomic because the
    /// temporary file is in the same directory, hence on the same filesystem.
    /// This matters more since the version-check cadence writes daily rather than
    /// only at login, and the token it would take with it lasts 30 days with no
    /// refresh.
    /// </summary>
    public void Save(AppConfig config)
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, AppJsonContext.Default.AppConfig);
        // Process id, not a random name: two processes writing at once each get their
        // own file, and a leftover from a killed process is identifiable.
        var temp = $"{_configPath}.{Environment.ProcessId}.tmp";
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, _configPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public AppConfig Resolve(
        string? flagServer = null,
        string? flagToken = null,
        Func<string, string?>? envLookup = null)
    {
        envLookup ??= Environment.GetEnvironmentVariable;
        var fileConfig = Load();

        return new AppConfig
        {
            Server = flagServer
                ?? envLookup("GRIMOIRE_SERVER")
                ?? fileConfig.Server,
            AccessToken = flagToken
                ?? envLookup("GRIMOIRE_TOKEN")
                ?? fileConfig.AccessToken,
            LastVersionCheck = fileConfig.LastVersionCheck,
            LastServerVersion = fileConfig.LastServerVersion
        };
    }

    /// <summary>
    /// Records a version observation by read-modify-write of the config file.
    /// Deliberately reads <see cref="Load"/> rather than a resolved config:
    /// <see cref="Resolve"/> merges GRIMOIRE_SERVER and GRIMOIRE_TOKEN from the
    /// environment, and persisting those would write a token to disk that the
    /// operator chose to keep out of it.
    /// </summary>
    public void UpdateVersionCheck(string? serverVersion, DateTimeOffset checkedAt)
    {
        var onDisk = Load();
        onDisk.LastServerVersion = serverVersion;
        onDisk.LastVersionCheck = checkedAt;
        Save(onDisk);
    }
}
