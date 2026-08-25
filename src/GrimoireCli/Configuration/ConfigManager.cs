using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Configuration;

/// <summary>The config file could not be written, with a message fit to print.</summary>
public class ConfigWriteException : Exception
{
    public ConfigWriteException(string message, Exception inner) : base(message, inner) { }
}

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
    /// Moves an unparseable config aside before anything can overwrite it. The refresh
    /// token it holds is what keeps the session alive, and a file broken by a
    /// hand-edit usually still contains it — but the next write would replace the file
    /// wholesale, so leaving it in place would destroy on the following command what
    /// the warning invites the operator to repair. Moving it also means the warning is
    /// printed once rather than by every subsequent <see cref="Load"/> in the process.
    /// </summary>
    private void QuarantineUnparseableConfig(JsonException ex)
    {
        var quarantine = $"{_configPath}.corrupt";
        try
        {
            File.Move(_configPath, quarantine, overwrite: true);
            _logger.Warn($"{_configPath} is not valid JSON ({ex.Message}). Moved it to "
                         + $"{quarantine} and continuing without it. Run: grimoire-cli login");
        }
        catch (Exception moveFailure) when (moveFailure is IOException or UnauthorizedAccessException)
        {
            _logger.Warn($"Ignoring {_configPath}: it is not valid JSON ({ex.Message}). "
                         + $"Could not move it aside ({moveFailure.Message}). Run: grimoire-cli login");
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
    public void Save(AppConfig config)
    {
        var dir = Path.GetDirectoryName(_configPath);
        var json = JsonSerializer.Serialize(config, AppJsonContext.Default.AppConfig);
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

    public AppConfig Resolve(
        string? flagServer = null,
        string? flagToken = null,
        Func<string, string?>? envLookup = null)
    {
        envLookup ??= Environment.GetEnvironmentVariable;
        var fileConfig = Load();
        var tokenOverride = flagToken ?? envLookup("GRIMOIRE_TOKEN");
        return new AppConfig
        {
            Server = flagServer
                ?? envLookup("GRIMOIRE_SERVER")
                ?? fileConfig.Server,
            AccessToken = tokenOverride ?? fileConfig.AccessToken,
            // The stored cookie renews the session it was issued for, so it
            // travels only with the access token from the same file.
            RefreshToken = tokenOverride == null ? fileConfig.RefreshToken : null,
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

    /// <summary>
    /// Persists a refreshed token pair by read-modify-write of the config file,
    /// for the same reason as <see cref="UpdateVersionCheck"/>: writing a
    /// resolved config would put a GRIMOIRE_TOKEN value on disk that the operator
    /// chose to keep out of it. A null <paramref name="refreshToken"/> leaves the
    /// stored one in place — the server rotates on every refresh, so the value
    /// already on disk is the best credential available if a response carried no
    /// new cookie.
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
