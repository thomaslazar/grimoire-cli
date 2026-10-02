using System.CommandLine;
using GrimoireCli.Api;
using GrimoireCli.Configuration;

namespace GrimoireCli.Commands;

public enum KeyLoginOutcome { Valid, ValidWithoutLibrary, Rejected }

public static class LoginCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var serverOption = new Option<string?>("--server") { Description = "Grimoire server URL; falls back to GRIMOIRE_SERVER, then prompts" };
        var usernameOption = new Option<string?>("--username") { Description = "Username (prompts if omitted)" };
        var passwordOption = new Option<string?>("--password") { Description = "Password — visible in process list / shell history; prefer --password-stdin" };
        var passwordStdinOption = new Option<bool>("--password-stdin") { Description = "Read the password from the first line of stdin" };
        var apiKeyStdinOption = new Option<bool>("--api-key-stdin") { Description = "Read an API key from the first line of stdin instead of a password" };
        var command = new Command("login", "Authenticate with a Grimoire server")
        {
            serverOption, usernameOption, passwordOption, passwordStdinOption, apiKeyStdinOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--password is visible in the process list and shell history. Prefer",
            "--password-stdin (reads the first line of stdin) for scripted use.",
            "The session refreshes itself; log in again only after 30 days idle,",
            "or if the session is revoked (password change, admin edit).",
            "OIDC accounts cannot log in here — this is the local password path.",
            "Writes the resolved config file; config get reports which one.",
            "",
            "--api-key-stdin logs in with a Grimoire API key instead: no session to",
            "renew, limited to the key's permissions, and only until it expires.",
            "Caveats:",
            "https://github.com/thomaslazar/grimoire-cli/blob/main/docs/authentication.md#api-keys",
            "Permission each command needs (Key column):",
            "https://github.com/thomaslazar/grimoire-cli/blob/main/docs/grimoire-api-coverage.md");
        command.AddExamples(
            "grimoire-cli login --server https://grimoire.example.com",
            "grimoire-cli login --server https://grimoire.example.com --username agent --password-stdin <<<\"$GRIMOIRE_PW\"",
            "grimoire-cli login --server https://grimoire.example.com --api-key-stdin <<<\"$GRIMOIRE_KEY\"");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            // GRIMOIRE_SERVER before the prompt, so the variable that serves every
            // other command also serves the one that establishes the server — and so
            // an unattended login has a way in that is not the flag. The prompt is
            // last because a non-interactive caller gets null from ReadLine and the
            // empty-answer guard below turns that into a readable exit.
            var server = parseResult.GetValue(serverOption)
                ?? Environment.GetEnvironmentVariable("GRIMOIRE_SERVER");
            var configManager = new ConfigManager();
            if (server == null)
            {
                Console.Error.Write("Server URL: ");
                server = Console.ReadLine()?.Trim();
            }
            if (string.IsNullOrEmpty(server))
            {
                _logger.Error("Server URL is required.");
                Environment.Exit(1);
            }
            var usernameFlag = parseResult.GetValue(usernameOption);
            var passwordFlag = parseResult.GetValue(passwordOption);
            var passwordStdin = parseResult.GetValue(passwordStdinOption);
            if (parseResult.GetValue(apiKeyStdinOption))
            {
                var conflict = KeyFlagConflict(usernameFlag != null, passwordFlag != null, passwordStdin);
                if (conflict != null)
                {
                    _logger.Error(conflict);
                    Environment.Exit(1);
                }
                return await LoginWithKeyAsync(server!, configManager);
            }
            if (passwordFlag != null && passwordStdin)
            {
                _logger.Error("Provide --password or --password-stdin, not both.");
                Environment.Exit(1);
            }
            var username = usernameFlag;
            if (string.IsNullOrEmpty(username))
            {
                Console.Error.Write("Username: ");
                username = Console.ReadLine()?.Trim();
            }
            string? password;
            if (passwordFlag != null)
            {
                password = passwordFlag;
            }
            else if (passwordStdin)
            {
                password = ReadPasswordFromStdin(Console.In);
                if (string.IsNullOrEmpty(password))
                {
                    _logger.Error("No password on stdin.");
                    Environment.Exit(1);
                }
            }
            else
            {
                Console.Error.Write("Password: ");
                password = ReadPassword();
            }
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                _logger.Error("Username and password are required.");
                Environment.Exit(1);
            }
            var client = new GrimoireApiClient(new AppConfig { Server = server });
            AppConfig config;
            try
            {
                var (body, refreshToken) = await client.LoginAsync(username!, password!);
                var token = GrimoireApiClient.ExtractToken(body);
                if (token == null)
                {
                    _logger.Error("Login succeeded but no token was found in the response. Body follows.");
                    Console.Error.WriteLine(body);
                    Environment.Exit(2);
                }
                config = configManager.Load();
                config.Server = server;
                config.AccessToken = token;
                config.ApiKey = null;
                // A server that issues no refresh cookie must clear any stale one,
                // so this is assigned unconditionally.
                config.RefreshToken = refreshToken;
                try
                {
                    configManager.Save(config);
                }
                catch (ConfigWriteException ex)
                {
                    // The login worked, but a token that was never written is a token
                    // the next command cannot use — report it as the failure it is.
                    _logger.Error(ex.Message);
                    Environment.Exit(1);
                }
                // With a refresh token stored, the access token's own 30 minutes are
                // renewed as needed, so its date would misreport how long the login
                // lasts. A server that issues no cookie (1.5.6) hands out a 30-day
                // token, where the date is what the operator needs.
                if (!string.IsNullOrEmpty(refreshToken))
                {
                    Console.Error.WriteLine($"Logged in to {server} (session renews automatically)");
                }
                else
                {
                    var expiry = TokenHelper.GetExpiration(token!);
                    Console.Error.WriteLine(expiry != null
                        ? $"Logged in to {server} (token expires {expiry:yyyy-MM-dd})"
                        : $"Logged in to {server}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.Error($"Login failed: {ex.Message}");
                Environment.Exit(2);
                throw;
            }
            // The token is already saved at this point, so a failure here is not a
            // login failure — it's a warning, not a reason to report exit 2 and make
            // the caller think they need to log in again. /api/about requires the
            // token just saved and carries the server version. The probe catches its
            // own failures (ProbeServerVersionAsync) and never throws, so there is
            // nothing to catch here.
            var authed = new GrimoireApiClient(config, configManager);
            if (await authed.CheckVersionNowAsync() is null)
                _logger.Warn("Logged in, but could not check the server version. Run with --debug for the reason.");
            return 0;
        });
        return command;
    }

    private static async Task<int> LoginWithKeyAsync(string server, ConfigManager configManager)
    {
        var key = ReadPasswordFromStdin(Console.In).Trim();
        if (key.Length == 0)
        {
            _logger.Error("No API key on stdin.");
            Environment.Exit(1);
        }
        // The key is probed before anything is saved, so a rejected key leaves
        // the existing login in place.
        var (status, body) = await new GrimoireApiClient(new AppConfig { Server = server, ApiKey = key }).ProbeAboutAsync();
        var outcome = ClassifyKeyProbe(status, body);
        if (outcome == KeyLoginOutcome.Rejected)
        {
            _logger.Error(status switch
            {
                null => $"Cannot reach the Grimoire server at {server}.",
                401 or 403 => $"API key rejected: {status} {body}",
                _ => $"API key check failed: {status} {body}"
            });
            Environment.Exit(2);
        }
        var config = configManager.Load();
        config.Server = server;
        config.ApiKey = key;
        config.AccessToken = null;
        config.RefreshToken = null;
        try
        {
            configManager.Save(config);
        }
        catch (ConfigWriteException ex)
        {
            _logger.Error(ex.Message);
            Environment.Exit(1);
        }
        Console.Error.WriteLine($"Logged in to {server} with an API key");
        if (outcome == KeyLoginOutcome.Valid)
            new GrimoireApiClient(config, configManager).RecordServerVersion(GrimoireApiClient.ReadStringProperty(body, "version"));
        else
            _logger.Warn("Logged in, but this key cannot read library, so the server version is not checked.");
        return 0;
    }

    /// <summary>
    /// /api/about requires library read, so a 403 naming that missing level
    /// proves the key authenticated even though it cannot report the version.
    /// Every other 403 (keys disabled, not allowed for the owner) is a refusal.
    /// </summary>
    internal static KeyLoginOutcome ClassifyKeyProbe(int? status, string body) => status switch
    {
        200 => KeyLoginOutcome.Valid,
        403 when body.Contains("access to 'library'") => KeyLoginOutcome.ValidWithoutLibrary,
        _ => KeyLoginOutcome.Rejected
    };

    internal static string? KeyFlagConflict(bool username, bool password, bool passwordStdin) =>
        username || password || passwordStdin
            ? "Use --api-key-stdin on its own, without --username or a password."
            : null;

    /// <summary>
    /// Read a password from stdin: the first line, stripped of a single
    /// trailing CRLF/LF. Returns "" if stdin is empty. A password with an
    /// embedded newline is not supportable via this path.
    /// </summary>
    internal static string ReadPasswordFromStdin(TextReader reader)
    {
        var line = reader.ReadLine();
        return line ?? "";
    }

    private static string ReadPassword()
    {
        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                password.Remove(password.Length - 1, 1);
            else if (key.Key != ConsoleKey.Backspace)
                password.Append(key.KeyChar);
        }
        Console.Error.WriteLine();
        return password.ToString();
    }
}
