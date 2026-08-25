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
    /// <see cref="EnsureValidTokenAsync"/>, and throw <c>ApiException</c> with the
    /// response body discarded on failure.
    /// </summary>
    public Generated.GrimoireApiClient Api { get; }

    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(100);

    public GrimoireApiClient(AppConfig config, ConfigManager? configManager = null,
        HttpMessageHandler? innerHandler = null)
    {
        _config = config;
        _configManager = configManager ?? new ConfigManager();
        // The CLI reads the refresh cookie off the login response itself and
        // stores it, so cookie handling stays here rather than in a container
        // whose contents die with the process.
        var debugHandler = new DebugHttpHandler(
            innerHandler ?? new HttpClientHandler { UseCookies = false });
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
    /// rather than by a generated model. See <see cref="ExtractToken"/>. The refresh
    /// token is not in the body at all: it arrives as a <c>Set-Cookie</c> header and
    /// is returned alongside, per <see cref="ExtractCookie"/>.
    /// </summary>
    public async Task<(string Body, string? RefreshToken)> LoginAsync(string username, string password)
    {
        var body = new Generated.Models.LoginRequest { Username = username, Password = password };
        var info = Api.Api.Auth.Login.ToPostRequestInformation(body);
        using var cts = new CancellationTokenSource(DefaultRequestTimeout);
        var request = await _adapter.ConvertToNativeRequestAsync<HttpRequestMessage>(info, cts.Token)
            ?? throw new InvalidOperationException("Failed to build login request");
        var response = await _http.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync(cts.Token);
        return (responseBody, ReadRefreshCookie(response));
    }

    /// <summary>The rotated refresh token from a login or refresh response.</summary>
    private static string? ReadRefreshCookie(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? ExtractCookie(cookies, RefreshCookieName)
            : null;

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

    /// <summary>The refresh token's cookie name, per <c>REFRESH_COOKIE_NAME</c>.</summary>
    internal const string RefreshCookieName = "grimoire_refresh";

    /// <summary>
    /// Reads one cookie's value out of a response's Set-Cookie headers. Grimoire
    /// delivers the refresh token only this way, so this is the sole path by
    /// which the CLI obtains one. Returns the text between "name=" and the first
    /// ";", which is empty when the server is clearing the cookie.
    /// </summary>
    internal static string? ExtractCookie(IEnumerable<string> setCookieHeaders, string name)
    {
        var prefix = name + "=";
        foreach (var header in setCookieHeaders)
        {
            if (!header.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var value = header[prefix.Length..];
            var end = value.IndexOf(';');
            return end < 0 ? value : value[..end];
        }
        return null;
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
    /// Whether to renew before sending. The threshold matches abs-cli's, and a
    /// stored cookie is required because there is nothing else to renew with.
    /// </summary>
    internal static bool ShouldRefreshProactively(string? accessToken, bool haveRefreshToken)
        => haveRefreshToken
           && !string.IsNullOrEmpty(accessToken)
           && TokenHelper.IsExpiringSoon(accessToken, thresholdSeconds: 60);

    /// <summary>
    /// Whether a failed response is the one kind of 401 a refresh can fix.
    /// Grimoire marks an expired access token with X-Token-Expired specifically
    /// so it stays distinguishable from "not authenticated" and "invalid token",
    /// and the refresh endpoint is rate-limited, so the other 401s are left alone.
    /// </summary>
    internal static bool ShouldRefreshOn401(HttpResponseMessage response, bool haveRefreshToken)
        => haveRefreshToken
           && response.StatusCode == System.Net.HttpStatusCode.Unauthorized
           && response.Headers.Contains("X-Token-Expired");

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
    /// messages include, and leaves EnsureSuccessAsync unchanged. Every response
    /// is passed through <see cref="EnsureJson"/> before it is returned, so a
    /// non-JSON body (Grimoire's SPA catch-all) exits 2 here rather than
    /// reaching a caller that will print it verbatim.
    /// </summary>
    public async Task<string> SendAsync(RequestInformation info, string? permissionHint = null, string? notFoundHint = null, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? DefaultRequestTimeout);
        await PreflightAsync(cts.Token);
        var response = await SendWithRefreshAsync(info, cts.Token);
        await EnsureSuccessAsync(response, permissionHint, notFoundHint);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        EnsureJson(body, info.URI.PathAndQuery);
        return body;
    }

    /// <summary>
    /// Sends a request, and if the access token turns out to have expired,
    /// renews it and sends the request once more. The retry rebuilds the native
    /// request from the same <see cref="RequestInformation"/> — an
    /// HttpRequestMessage cannot be resent — and rewinds the body stream first so
    /// the second attempt carries the same content as the first. Every body the
    /// CLI sends is a seekable MemoryStream — the SetStreamContent call sites in
    /// Services wrap one over an in-memory byte array, and the generated builders
    /// use SetContentFromParsable. A body that reports otherwise is not replayed.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRefreshAsync(
        RequestInformation info, CancellationToken cancellationToken)
    {
        var request = await _adapter.ConvertToNativeRequestAsync<HttpRequestMessage>(info, cancellationToken)
            ?? throw new InvalidOperationException($"Failed to build request for {info.URI.AbsolutePath}");
        var response = await _http.SendAsync(request, cancellationToken);
        if (!ShouldRefreshOn401(response, HasRefreshToken)) return response;
        if (info.Content is { CanSeek: false })
        {
            // Replaying needs the body back, and this one cannot be rewound. The
            // 401 stands, and the next invocation renews before it sends.
            _logger.Debug($"not replaying {info.URI.AbsolutePath}: the request body cannot be rewound");
            return response;
        }
        _logger.Debug($"access token expired on {info.URI.AbsolutePath}, refreshing and retrying");
        await RefreshAsync(cancellationToken);
        if (info.Content != null)
            info.Content.Position = 0;
        var retry = await _adapter.ConvertToNativeRequestAsync<HttpRequestMessage>(info, cancellationToken)
            ?? throw new InvalidOperationException($"Failed to rebuild request for {info.URI.AbsolutePath}");
        return await _http.SendAsync(retry, cancellationToken);
    }

    /// <summary>
    /// A response whose body is bytes rather than JSON. Identical to SendAsync
    /// through preflight, permission hints and error handling; only the read
    /// differs. The caller owns the returned stream.
    /// </summary>
    public async Task<Stream> SendStreamAsync(RequestInformation info, string? permissionHint = null, string? notFoundHint = null, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? DefaultRequestTimeout);
        await PreflightAsync(cts.Token);
        var response = await SendWithRefreshAsync(info, cts.Token);
        await EnsureSuccessAsync(response, permissionHint, notFoundHint);
        return await response.Content.ReadAsStreamAsync(cts.Token);
    }

    internal static string TruncateForLogging(string body, int maxChars = 500)
        => body.Length > maxChars
            ? $"{body[..maxChars]}... (truncated, {body.Length} chars total)"
            : body;

    private bool HasRefreshToken => !string.IsNullOrEmpty(_config.RefreshToken);

    /// <summary>
    /// Exchanges the stored refresh cookie for a new token pair. The endpoint
    /// authenticates on the cookie alone; the stale bearer header that
    /// HttpClient attaches from its defaults is ignored by the server. Any
    /// failure is terminal for this command — the session is gone and only a
    /// fresh login can replace it.
    /// </summary>
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var info = Api.Api.Auth.Refresh.ToPostRequestInformation();
        var request = await _adapter.ConvertToNativeRequestAsync<HttpRequestMessage>(info, cancellationToken)
            ?? throw new InvalidOperationException("Failed to build refresh request");
        request.Headers.Add("Cookie", $"{RefreshCookieName}={_config.RefreshToken}");
        var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.Debug($"refresh rejected: {(int)response.StatusCode} "
                          + $"{TruncateForLogging(await response.Content.ReadAsStringAsync(cancellationToken))}");
            SessionExpired();
        }
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var token = ExtractToken(body);
        if (token == null)
        {
            _logger.Debug($"refresh response carried no token: {TruncateForLogging(body)}");
            SessionExpired();
        }
        var rotated = ReadRefreshCookie(response);
        _config.AccessToken = token;
        if (!string.IsNullOrEmpty(rotated))
            _config.RefreshToken = rotated;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            _configManager.UpdateTokens(token!, string.IsNullOrEmpty(rotated) ? null : rotated);
        }
        catch (ConfigWriteException ex)
        {
            // This command still works, but the rotated token is the only live
            // one and it is not on disk, so the next invocation starts from a
            // credential the server has already retired.
            _logger.Warn($"{ex.Message} The next command may need a fresh login.");
        }
        _logger.Debug("token refresh succeeded");
    }

    /// <summary>
    /// Reports a session that can no longer be renewed. Routine rather than
    /// exceptional: the server also revokes on a password change or an admin edit.
    /// </summary>
    private static void SessionExpired()
    {
        _logger.Error("Session expired. Run: grimoire-cli login");
        Environment.Exit(2);
    }

    /// <summary>
    /// Renews the access token before sending when it is nearly out and a stored
    /// cookie can renew it. With no cookie — a token from a flag or the
    /// environment, or a 1.5.6 server that issues none — the request still goes
    /// out and the server answers with its own 401.
    /// </summary>
    private async Task EnsureValidTokenAsync(CancellationToken cancellationToken)
    {
        var token = _http.DefaultRequestHeaders.Authorization?.Parameter;
        if (token == null) return;
        if (ShouldRefreshProactively(token, HasRefreshToken))
        {
            _logger.Debug($"access token expiring in {TokenHelper.SecondsUntilExpiry(token)}s, refreshing");
            await RefreshAsync(cancellationToken);
            return;
        }
        if (TokenHelper.IsExpiringSoon(token, thresholdSeconds: 60))
            _logger.Warn("Access token has expired or is about to. Run: grimoire-cli login");
        else
            _logger.Debug($"access token valid ({TokenHelper.SecondsUntilExpiry(token)}s remaining)");
    }

    /// <summary>
    /// Runs before every request. The token renewal is per-request because it is
    /// local and a long command can cross an expiry mid-run; the version check is
    /// once per client, and at most once a day across processes.
    /// </summary>
    private async Task PreflightAsync(CancellationToken cancellationToken)
    {
        await EnsureValidTokenAsync(cancellationToken);
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
        else if (IsComparableVersion(observed))
            _logger.Debug($"server version {observed} (in tested range {MinSupportedVersion}-{MaxTestedVersion})");
        else
            _logger.Debug($"server version {observed} carries no version number to compare against the tested range {MinSupportedVersion}-{MaxTestedVersion}");

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

        if (!IsComparableVersion(observed))
            return string.IsNullOrEmpty(moved) ? null : moved.TrimEnd();

        if (CompareVersions(observed, MinSupportedVersion) < 0)
            return $"{moved}Grimoire server version {observed} is older than the minimum supported version "
                   + $"({MinSupportedVersion}). Some features may not work.";

        if (CompareVersions(observed, MaxTestedVersion) > 0)
            return $"{moved}grimoire-cli {ClientVersion} was tested up to Grimoire {MaxTestedVersion}; "
                   + $"this server is {observed}. Check for a newer grimoire-cli.";

        return null;
    }

    /// <summary>
    /// Whether a version string carries anything to compare. The nightly and edge
    /// channels report their channel name rather than a version, and
    /// <see cref="ParseVersion"/> reads a non-numeric segment as 0 — so comparing
    /// one would report "older than the minimum" about a string that says nothing
    /// of the sort. A literal "0.0.0" parses to all zeros too and so also reads as
    /// uncomparable, which is harmless because no Grimoire release ships as version 0.
    /// </summary>
    internal static bool IsComparableVersion(string? version)
        => !string.IsNullOrWhiteSpace(version) && ParseVersion(version).Any(p => p != 0);

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
