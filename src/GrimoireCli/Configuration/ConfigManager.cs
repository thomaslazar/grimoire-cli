using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Configuration;

/// <summary>The config file could not be written, with a message fit to print.</summary>
public class ConfigWriteException : Exception
{
    public ConfigWriteException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// The config file in use and the tier that chose it: <c>env</c> for
/// GRIMOIRE_CONFIG, <c>binary</c> for grimoire-cli.json beside the executable,
/// <c>home</c> for ~/.grimoire-cli/config.json.
/// </summary>
public sealed record ConfigLocation(string Path, string Source);

public class ConfigManager
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly string _configPath;

    public ConfigManager(string configPath)
    {
        _configPath = configPath;
    }

    public ConfigManager() : this(DefaultConfigPath()) { }

    public static string DefaultConfigPath() => Locate().Path;

    /// <summary>
    /// Resolves the config file: GRIMOIRE_CONFIG if set; else grimoire-cli.json
    /// beside the running executable, but only if it already exists, so an
    /// install never claims a config it was not given; else the home default.
    /// The token must live in a file the CLI can write renewals back to, which is
    /// why the choice is of a file and never of a token.
    /// </summary>
    public static ConfigLocation Locate() => Locate(
        Environment.GetEnvironmentVariable,
        Environment.ProcessPath,
        File.Exists,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    internal static ConfigLocation Locate(
        Func<string, string?> envLookup, string? executablePath, Func<string, bool> fileExists, string home)
    {
        var fromEnv = envLookup("GRIMOIRE_CONFIG");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return new ConfigLocation(Path.GetFullPath(fromEnv), "env");
        var exeDir = executablePath is null ? null : Path.GetDirectoryName(executablePath);
        if (exeDir is not null)
        {
            var sibling = Path.Combine(exeDir, "grimoire-cli.json");
            if (fileExists(sibling))
                return new ConfigLocation(sibling, "binary");
        }
        return new ConfigLocation(Path.Combine(home, ".grimoire-cli", "config.json"), "home");
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
            QuarantineUnparseableConfig(ex);
            return new AppConfig();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn($"Could not read {_configPath}: {ex.Message}");
            return new AppConfig();
        }
    }

    /// <summary>
    /// Copies an unparseable config to <c>&lt;path&gt;.corrupt</c>, then resets the
    /// original to an empty config through the same atomic write path <see cref="Save"/>
    /// uses. The copy keeps a hand-edit's refresh token recoverable; resetting rather
    /// than deleting keeps the warning to one print, since the next <see cref="Load"/>
    /// then finds a valid, empty file rather than an absent one it would warn about
    /// again. Leaving the path claimed is what matters for the sibling tier: an install
    /// with its own grimoire-cli.json must fail "not authenticated" on its own account
    /// rather than have the path disappear and fall back to the home config. For the
    /// home and env tiers a reset file loads exactly as an absent one would, so this is
    /// not a behaviour change there — only the sibling tier depends on the path staying
    /// claimed.
    /// </summary>
    private void QuarantineUnparseableConfig(JsonException ex)
    {
        var quarantine = $"{_configPath}.corrupt";
        try
        {
            File.Copy(_configPath, quarantine, overwrite: true);
        }
        catch (Exception copyFailure) when (copyFailure is IOException or UnauthorizedAccessException)
        {
            _logger.Warn($"Ignoring {_configPath}: it is not valid JSON ({ex.Message}). "
                         + $"Could not copy it aside ({copyFailure.Message}). Run: grimoire-cli login");
            return;
        }

        try
        {
            WriteAtomic(JsonSerializer.Serialize(new AppConfig(), AppJsonContext.Default.AppConfig));
            _logger.Warn($"{_configPath} is not valid JSON ({ex.Message}). Copied it to "
                         + $"{quarantine} and reset it to an empty config. Run: grimoire-cli login");
        }
        catch (ConfigWriteException resetFailure)
        {
            _logger.Warn($"{_configPath} is not valid JSON ({ex.Message}). Copied it to "
                         + $"{quarantine}, but could not reset it ({resetFailure.Message}). "
                         + "Run: grimoire-cli login");
        }
    }

    /// <summary>
    /// Writes the config by filling a temporary file beside it and replacing the
    /// target with it, so a reader never sees a half-written file and a process that
    /// dies mid-write cannot destroy the token already there. Both paths are in the
    /// same directory, hence the same filesystem, which is what makes the replacement
    /// atomic. This matters more since the version-check cadence writes daily rather
    /// than only at login, and losing the refresh token it would take with it costs a
    /// login. A power loss is not covered — the rename can land before the data —
    /// but a truncated file is read as absent rather than as an error.
    /// </summary>
    /// <exception cref="ConfigWriteException">
    /// The config could not be written. Callers that promise persistence — login,
    /// config set — must report this rather than claim success; the version-check
    /// cadence swallows it, because a diagnostic may not fail the command it precedes.
    /// </exception>
    public void Save(AppConfig config) =>
        WriteAtomic(JsonSerializer.Serialize(config, AppJsonContext.Default.AppConfig));

    private void WriteAtomic(string json)
    {
        var dir = Path.GetDirectoryName(_configPath);
        // Process id, not a random name: concurrent writers each get their own file,
        // and a leftover from a killed process is identifiable. Concurrent writes are
        // still last-one-wins as a whole — the replacement makes each write complete,
        // not the read-modify-write around it atomic.
        var temp = $"{_configPath}.{Environment.ProcessId}.tmp";
        try
        {
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(temp, json);
            // The file carries a bearer token, so restrict it before it becomes the
            // config: replacing the target swaps in this file's mode, which would
            // otherwise be whatever the umask allows — and would silently undo an
            // operator's chmod on every write.
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            // Replace where the target exists: on Windows it is the call with the
            // documented atomic-replacement semantics, and on Unix both are rename(2).
            if (File.Exists(_configPath))
                File.Replace(temp, _configPath, destinationBackupFileName: null);
            else
                File.Move(temp, _configPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Name the real config first: the underlying message names the temporary
            // file, which the operator never chose and would not recognise on its own.
            throw new ConfigWriteException(
                $"Could not write {_configPath} (written via a temporary file beside it): {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public AppConfig Resolve(Func<string, string?>? envLookup = null)
    {
        envLookup ??= Environment.GetEnvironmentVariable;
        var fileConfig = Load();
        return new AppConfig
        {
            Server = envLookup("GRIMOIRE_SERVER") ?? fileConfig.Server,
            AccessToken = fileConfig.AccessToken,
            RefreshToken = fileConfig.RefreshToken,
            LastVersionCheck = fileConfig.LastVersionCheck,
            LastServerVersion = fileConfig.LastServerVersion
        };
    }

    /// <summary>
    /// Records a version observation by read-modify-write of the config file.
    /// Deliberately reads <see cref="Load"/> rather than a resolved config:
    /// <see cref="Resolve"/> merges GRIMOIRE_SERVER from the environment, and
    /// persisting it would write a server to disk that the operator chose to
    /// keep out of the file.
    /// </summary>
    public void UpdateVersionCheck(string? serverVersion, DateTimeOffset checkedAt)
    {
        var onDisk = Load();
        onDisk.LastServerVersion = serverVersion;
        onDisk.LastVersionCheck = checkedAt;
        Save(onDisk);
    }

    /// <summary>
    /// Persists a refreshed token pair by read-modify-write of the config file,
    /// for the same reason as <see cref="UpdateVersionCheck"/>: writing a
    /// resolved config would put a GRIMOIRE_SERVER value on disk that the
    /// operator chose to keep out of the file. A null
    /// <paramref name="refreshToken"/> leaves the stored one in place — the
    /// server rotates on every refresh, so the value already on disk is the best
    /// credential available if a response carried no new cookie.
    /// </summary>
    public void UpdateTokens(string accessToken, string? refreshToken)
    {
        var onDisk = Load();
        onDisk.AccessToken = accessToken;
        if (refreshToken != null)
            onDisk.RefreshToken = refreshToken;
        Save(onDisk);
    }
}
