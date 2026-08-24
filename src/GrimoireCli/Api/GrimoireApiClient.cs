using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using GrimoireCli.Configuration;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace GrimoireCli.Api;

public class GrimoireApiClient
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly HttpClient _http;
    private readonly IRequestAdapter _adapter;
    private readonly AppConfig _config;
    private readonly ConfigManager _configManager;
    private bool _versionCheckDone;

    /// <summary>
    /// The generated request builders. They construct URLs, query strings and
    /// bodies from the OpenAPI spec; this class still owns sending and errors.
    /// Build requests with a builder's <c>ToXRequestInformation</c> method and send
    /// them through <see cref="SendAsync(RequestInformation, string?, string?, TimeSpan?)"/>
    /// — never call a generated execute method (e.g. <c>GetAsync</c>) directly. Those
    /// bypass <see cref="EnsureSuccessAsync"/>, the exit-code mapping and
    /// <see cref="WarnIfTokenExpired"/>, and throw <c>ApiException</c> with the
    /// response body discarded on failure.
    /// </summary>
    public Generated.GrimoireApiClient Api { get; }

    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(100);

    public GrimoireApiClient(AppConfig config, ConfigManager? configManager = null)
    {
        _config = config;
        _configManager = configManager ?? new ConfigManager();
        var debugHandler = new DebugHttpHandler(new HttpClientHandler());
        _http = new HttpClient(debugHandler)
        {
            // Every request now carries an absolute URI from the generated builders, so
            // this no longer does any routing — but it still does two real jobs: the
            // `Uri` constructor validates `config.Server` eagerly, so a malformed
            // `--server` throws here rather than at first send, and the debug line below
            // reads it. Do not delete it, and do not "keep it in sync" with
            // `_adapter.BaseUrl`, which correctly has no trailing slash.
            BaseAddress = new Uri(config.Server!.TrimEnd('/') + "/"),
            // Timeouts are managed per-request via CancellationTokenSource so long
            // operations (rescan, reindex) can opt into a longer budget.
            Timeout = Timeout.InfiniteTimeSpan
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"grimoire-cli/{ClientVersion}");

        if (config.AccessToken != null)
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", config.AccessToken);
        _logger.Debug($"client base address: {_http.BaseAddress}");

        // Kiota builds requests; we send them. The adapter is handed our own
        // HttpClient so the debug handler, User-Agent and bearer header apply,
        // and authentication stays on the default headers rather than moving to
        // a Kiota provider.
        _adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: _http)
        {
            BaseUrl = config.Server!.TrimEnd('/')
        };
        Api = new Generated.GrimoireApiClient(_adapter);
    }

    /// <summary>
    /// POST /api/auth/login. Returns the raw response body — the OpenAPI spec types
    /// this response as an empty schema, so the token key is located by inspection
    /// rather than by a generated model. See <see cref="ExtractToken"/>.
    /// </summary>
    public async Task<string> LoginAsync(string username, string password)
    {
        var body = new Generated.Models.LoginRequest { Username = username, Password = password };
        var info = Api.Api.Auth.Login.ToPostRequestInformation(body);

        using var cts = new CancellationTokenSource(DefaultRequestTimeout);
        var request = await _adapter.ConvertToNativeRequestAsync<HttpRequestMessage>(info, cts.Token)
            ?? throw new InvalidOperationException("Failed to build login request");
        var response = await _http.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cts.Token);
    }

    /// <summary>
    /// Pulls the JWT out of a login response. Grimoire's spec does not describe the
    /// body, so the common FastAPI spellings are tried in order. Returns null if the
    /// body carries no recognisable token, which the caller should treat as an error
    /// worth reporting verbatim — a silent miss here looks like a broken login.
    /// </summary>
    internal static string? ExtractToken(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var key in new[] { "access_token", "token", "accessToken" })
            {
                if (doc.RootElement.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String)
                    return el.GetString();
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads a top-level string property, or null. Used for untyped responses.</summary>
    internal static string? ReadStringProperty(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a top-level array property has at least one element. The bulk
    /// endpoints report per-item failures this way, and the exit code turns on
    /// nothing else about them.
    /// </summary>
    internal static bool HasItems(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(property, out var el)
                   && el.ValueKind == JsonValueKind.Array
                   && el.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a body is JSON, or empty. A 204 or other bodiless success is
    /// legitimate and parses as nothing. Pure so both branches are testable —
    /// <see cref="EnsureJson"/> cannot be, because it exits.
    /// </summary>
    internal static bool IsJsonOrEmpty(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Fails a response body that is not JSON, taking over the check the response
    /// DTOs used to provide as a side effect of deserializing. Grimoire's SPA
    /// catch-all answers an unroutable request (an empty, ".", or otherwise
    /// mis-encoded id) with an HTML 200, and without this that page would reach
    /// stdout as though it were the API's answer.
    /// </summary>
    internal static void EnsureJson(string json, string endpoint)
    {
        if (IsJsonOrEmpty(json)) return;
        _logger.Debug($"unparseable body from {endpoint}: {TruncateForLogging(json)}");
        _logger.Error($"Response from {endpoint} could not be parsed as JSON. Run with --debug to see the response body.");
        Environment.Exit(2);
    }

    /// <summary>
    /// Sends a request built by a generated builder. Converting to a native
    /// HttpRequestMessage and sending it here — rather than through Kiota's
    /// SendPrimitiveAsync — keeps the response body on failures, which the error
    /// messages include, and leaves EnsureSuccessAsync unchanged.
    /// </summary>
    public async Task<string> SendAsync(RequestInformation info, string? permissionHint = null, string? notFoundHint = null, TimeSpan? timeout = null)
    {
        await PreflightAsync();
        using var cts = new CancellationTokenSource(timeout ?? DefaultRequestTimeout);
        var request = await _adapter.ConvertToNativeRequestAsync<HttpRequestMessage>(info, cts.Token)
            ?? throw new InvalidOperationException($"Failed to build request for {info.URI.AbsolutePath}");
        var response = await _http.SendAsync(request, cts.Token);
        await EnsureSuccessAsync(response, permissionHint, notFoundHint);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        EnsureJson(body, info.URI.PathAndQuery);
        return body;
    }

    /// <summary>
    /// A response whose body is bytes rather than JSON. Identical to SendAsync
    /// through preflight, permission hints and error handling; only the read
    /// differs. The caller owns the returned stream.
    /// </summary>
    public async Task<Stream> SendStreamAsync(RequestInformation info, string? permissionHint = null, string? notFoundHint = null, TimeSpan? timeout = null)
    {
        await PreflightAsync();
        using var cts = new CancellationTokenSource(timeout ?? DefaultRequestTimeout);
        var request = await _adapter.ConvertToNativeRequestAsync<HttpRequestMessage>(info, cts.Token)
            ?? throw new InvalidOperationException($"Failed to build request for {info.URI.AbsolutePath}");
        var response = await _http.SendAsync(request, cts.Token);
        await EnsureSuccessAsync(response, permissionHint, notFoundHint);
        return await response.Content.ReadAsStreamAsync(cts.Token);
    }

    internal static string TruncateForLogging(string body, int maxChars = 500)
        => body.Length > maxChars
            ? $"{body[..maxChars]}... (truncated, {body.Length} chars total)"
            : body;

    // Grimoire issues a 30-day JWT and exposes no refresh endpoint, so there is
    // nothing to renew — the only remedy for an expired token is another login.
    // Warn early rather than letting the server answer 401 with no explanation.
    private void WarnIfTokenExpired()
    {
        var token = _http.DefaultRequestHeaders.Authorization?.Parameter;
        if (token == null) return;
        if (TokenHelper.IsExpiringSoon(token, thresholdSeconds: 60))
            _logger.Warn("Access token has expired or is about to. Run: grimoire-cli login");
        else
            _logger.Debug($"access token valid ({TokenHelper.SecondsUntilExpiry(token)}s remaining)");
    }

    /// <summary>
    /// Runs before every request. The token warning is per-request because it is
    /// local and a long command can cross an expiry mid-run; the version check is
    /// once per client, and at most once a day across processes.
    /// </summary>
    private async Task PreflightAsync()
    {
        WarnIfTokenExpired();
        await EnsureVersionCheckedAsync();
    }

    private async Task EnsureVersionCheckedAsync()
    {
        if (_versionCheckDone) return;
        _versionCheckDone = true;
        var now = DateTimeOffset.UtcNow;
        if (!ShouldCheckVersion(_config.LastVersionCheck, now))
        {
            _logger.Debug($"server version checked {_config.LastVersionCheck:u}, next due in "
                          + $"{VersionCheckInterval - (now - _config.LastVersionCheck!.Value):hh\\:mm}");
            return;
        }
        var observed = await ProbeServerVersionAsync();
        if (observed != null) RecordServerVersion(observed);
    }

    /// <summary>
    /// GET /api/about on its own short budget, deliberately outside the normal send
    /// path: a diagnostic may not exit the process through EnsureSuccessAsync, and
    /// routing it through PreflightAsync would re-enter the check that triggered it.
    /// Returns null on any failure, which leaves the timestamp alone so the next
    /// invocation retries.
    /// </summary>
    private async Task<string?> ProbeServerVersionAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(VersionProbeTimeout);
            var info = Api.Api.About.ToGetRequestInformation();
            var request = await _adapter.ConvertToNativeRequestAsync<HttpRequestMessage>(info, cts.Token);
            if (request == null) return null;
            var response = await _http.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Debug($"version probe: {(int)response.StatusCode} from /api/about");
                return null;
            }
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            return ReadStringProperty(body, "version");
        }
        catch (Exception ex)
        {
            // Unreachable, timed out, not JSON — all the same to a diagnostic. The
            // real command will report the outage a moment later if there is one.
            _logger.Debug($"version probe failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// One place owns "a version was observed", so the daily probe and login warn
    /// and persist identically.
    /// </summary>
    internal void RecordServerVersion(string? observed)
    {
        if (observed == null) return;
        var warning = VersionWarning(observed, _config.LastServerVersion);
        if (warning != null) _logger.Warn(warning);
        else _logger.Debug($"server version {observed} (in tested range {MinSupportedVersion}-{MaxTestedVersion})");

        var checkedAt = DateTimeOffset.UtcNow;
        try
        {
            _configManager.UpdateVersionCheck(observed, checkedAt);
        }
        catch (Exception ex)
        {
            // An unwritable config only costs a re-probe next invocation.
            _logger.Debug($"could not record the version check: {ex.Message}");
        }
        // Keep the in-memory config in step, so a later Save(_config) by the same
        // process cannot write the stale values back over what was just recorded.
        _config.LastServerVersion = observed;
        _config.LastVersionCheck = checkedAt;
    }

    /// <summary>
    /// Probes and records regardless of the interval. Used by login, where a fresh
    /// verdict is the point. Returns the observed version, or null if the probe
    /// failed, so the caller can tell "checked, in range" from "could not check".
    /// </summary>
    public async Task<string?> CheckVersionNowAsync()
    {
        _versionCheckDone = true;
        var observed = await ProbeServerVersionAsync();
        RecordServerVersion(observed);
        return observed;
    }

    private static readonly string MinSupportedVersion = "1.5.6";
    private static readonly string MaxTestedVersion = "1.5.6";

    // The informational version carries CI's build stamp ("0.1.0+pr-1.a1b2c3d") so
    // server logs identify which build called. It lives in an assembly-level
    // attribute, which Native AOT can trim — self-test asserts it still resolves.
    internal static readonly string ClientVersion =
        typeof(GrimoireApiClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(GrimoireApiClient).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    internal static readonly TimeSpan VersionCheckInterval = TimeSpan.FromHours(24);
    internal static readonly TimeSpan VersionProbeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// True when the version is worth re-checking: never checked, a full interval
    /// has passed, or the recorded time is in the future — which means the clock
    /// moved backwards, and treating that as fresh would park the check forever.
    /// </summary>
    internal static bool ShouldCheckVersion(DateTimeOffset? lastCheck, DateTimeOffset now)
        => lastCheck is null
           || now - lastCheck.Value >= VersionCheckInterval
           || lastCheck.Value > now;

    /// <summary>
    /// The warning for an observed server version, or null when there is nothing to
    /// say. Pure so the wording is testable without capturing logs. The check is
    /// provenance, not protection: it reports being off the tested versions and
    /// never blocks.
    /// </summary>
    internal static string? VersionWarning(string? observed, string? previous)
    {
        if (string.IsNullOrEmpty(observed)) return null;

        var moved = !string.IsNullOrEmpty(previous) && previous != observed
            ? $"This server moved from Grimoire {previous} to {observed} since the last check. "
            : "";

        if (CompareVersions(observed, MinSupportedVersion) < 0)
            return $"{moved}Grimoire server version {observed} is older than the minimum supported version "
                   + $"({MinSupportedVersion}). Some features may not work.";

        if (CompareVersions(observed, MaxTestedVersion) > 0)
            return $"{moved}grimoire-cli {ClientVersion} was tested up to Grimoire {MaxTestedVersion}; "
                   + $"this server is {observed}. Check for a newer grimoire-cli.";

        return null;
    }

    internal static int CompareVersions(string a, string b)
    {
        var aParts = ParseVersion(a);
        var bParts = ParseVersion(b);
        var len = Math.Max(aParts.Length, bParts.Length);
        for (int i = 0; i < len; i++)
        {
            var av = i < aParts.Length ? aParts[i] : 0;
            var bv = i < bParts.Length ? bParts[i] : 0;
            if (av != bv) return av.CompareTo(bv);
        }
        return 0;
    }

    // Tolerates a leading "v" and any pre-release suffix; a segment that isn't a
    // number counts as 0 rather than throwing, because an unparseable version
    // string must not take down an otherwise working command.
    private static int[] ParseVersion(string version)
        => version.TrimStart('v', 'V')
            .Split('.')
            .Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0)
            .ToArray();

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response, string? permissionHint = null, string? notFoundHint = null)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync();
        var status = (int)response.StatusCode;
        var message = status switch
        {
            401 => "Not authenticated, or the token has expired. Run: grimoire-cli login",
            403 when permissionHint != null => $"Permission denied. This operation requires {permissionHint}.",
            403 => $"Permission denied.{(string.IsNullOrWhiteSpace(body) ? "" : $" {body.Trim()}")}",
            400 => $"Bad request.{(string.IsNullOrWhiteSpace(body) ? "" : $" {body.Trim()}")}",
            404 when notFoundHint != null => $"Not found. {notFoundHint}",
            404 => $"Not found.{(string.IsNullOrWhiteSpace(body) ? "" : $" {body.Trim()}")}",
            422 => $"Validation error.{(string.IsNullOrWhiteSpace(body) ? "" : $" {body.Trim()}")}",
            _ => $"API request failed: {status} {response.ReasonPhrase}{(string.IsNullOrWhiteSpace(body) ? "" : $"\n{body.Trim()}")}"
        };
        _logger.Error(message);
        Environment.Exit(2);
    }
}
