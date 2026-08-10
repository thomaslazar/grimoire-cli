using System.CommandLine;
using GrimoireCli.Api;
using GrimoireCli.Configuration;

namespace GrimoireCli.Commands;

public static class LoginCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var serverOption = new Option<string?>("--server") { Description = "Grimoire server URL" };
        var usernameOption = new Option<string?>("--username") { Description = "Username (prompts if omitted)" };
        var passwordOption = new Option<string?>("--password") { Description = "Password — visible in process list / shell history; prefer --password-stdin" };
        var passwordStdinOption = new Option<bool>("--password-stdin") { Description = "Read the password from the first line of stdin" };
        var command = new Command("login", "Authenticate with a Grimoire server")
        {
            serverOption, usernameOption, passwordOption, passwordStdinOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--password is visible in the process list and shell history. Prefer",
            "--password-stdin (reads the first line of stdin) for scripted use.",
            "The JWT is valid 30 days and Grimoire has no refresh endpoint, so an",
            "expired token means logging in again.",
            "OIDC accounts cannot log in here — this is the local password path.");
        command.AddExamples(
            "grimoire-cli login --server https://grimoire.example.com",
            "grimoire-cli login --server https://grimoire.example.com --username agent --password-stdin <<<\"$GRIMOIRE_PW\"");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var server = parseResult.GetValue(serverOption);
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
                var body = await client.LoginAsync(username!, password!);
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
                configManager.Save(config);
                var expiry = TokenHelper.GetExpiration(token!);
                Console.Error.WriteLine(expiry != null
                    ? $"Logged in to {server} (token expires {expiry:yyyy-MM-dd})"
                    : $"Logged in to {server}");
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
            // token just saved and carries the server version.
            try
            {
                var authed = new GrimoireApiClient(config);
                var about = await authed.GetAsync(ApiEndpoints.About);
                GrimoireApiClient.CheckServerVersion(GrimoireApiClient.ReadStringProperty(about, "version"));
            }
            catch (HttpRequestException ex)
            {
                _logger.Warn($"Logged in, but could not check server version: {ex.Message}");
            }
            return 0;
        });
        return command;
    }

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
