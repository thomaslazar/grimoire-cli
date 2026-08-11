using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GrimoireCli.Configuration;
using GrimoireCli.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace GrimoireCli.Api;

public class GrimoireApiClient
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly HttpClient _http;
    private readonly IRequestAdapter _adapter;

    /// <summary>
    /// The generated request builders. They construct URLs, query strings and
    /// bodies from the OpenAPI spec; this class still owns sending and errors.
    /// </summary>
    public Generated.GrimoireApiClient Api { get; }

    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(100);

    public GrimoireApiClient(AppConfig config)
    {
        var debugHandler = new DebugHttpHandler(new HttpClientHandler());
        _http = new HttpClient(debugHandler)
        {
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
    /// Sends a request built by a generated builder. Converting to a native
    /// HttpRequestMessage and sending it here — rather than through Kiota's
    /// SendPrimitiveAsync — keeps the response body on failures, which the error
    /// messages include, and leaves EnsureSuccessAsync unchanged.
    /// </summary>
    public async Task<string> SendAsync(RequestInformation info, string? permissionHint = null, string? notFoundHint = null, TimeSpan? timeout = null)
    {
        WarnIfTokenExpired();
        using var cts = new CancellationTokenSource(timeout ?? DefaultRequestTimeout);
        var request = await _adapter.ConvertToNativeRequestAsync<HttpRequestMessage>(info, cts.Token)
            ?? throw new InvalidOperationException($"Failed to build request for {info.URI.AbsolutePath}");
        var response = await _http.SendAsync(request, cts.Token);
        await EnsureSuccessAsync(response, permissionHint, notFoundHint);
        return await response.Content.ReadAsStringAsync(cts.Token);
    }

    public async Task<T> SendAsync<T>(RequestInformation info, JsonTypeInfo<T> typeInfo, string? permissionHint = null, string? notFoundHint = null, TimeSpan? timeout = null)
    {
        var json = await SendAsync(info, permissionHint, notFoundHint, timeout);
        return Deserialize(json, typeInfo, info.URI.AbsolutePath);
    }

    // Shared by every typed overload above. Grimoire's SPA catch-all answers an
    // unroutable request (an empty, ".", or otherwise mis-encoded id) with an
    // HTML 200, not an API error — so deserialization is where that case must be
    // caught. Routes it through the same log-and-exit(2) mechanism as
    // EnsureSuccessAsync rather than letting JsonException surface as a raw
    // stack trace.
    internal static T Deserialize<T>(string json, JsonTypeInfo<T> typeInfo, string endpoint)
    {
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo)
                ?? throw new InvalidOperationException($"Failed to deserialize response from {endpoint}");
        }
        catch (JsonException ex)
        {
            // The body itself is the diagnostic that distinguishes an HTML SPA
            // catch-all from a truncated-but-otherwise-valid JSON response, and
            // ex.Message carries the line/byte position — but a full HTML page
            // or huge payload on stderr would flood the terminal, so it's
            // truncated and gated behind --debug rather than always shown.
            _logger.Debug($"unparseable body from {endpoint}: {TruncateForLogging(json)}");
            _logger.Debug($"JsonException: {ex.Message}");
            _logger.Error($"Response from {endpoint} could not be parsed as JSON. Run with --debug to see the response body and parse error.");
            Environment.Exit(2);
            throw;
        }
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

    private static readonly string MinSupportedVersion = "1.5.5";
    private static readonly string MaxTestedVersion = "1.5.5";

    // The informational version carries CI's build stamp ("0.1.0+pr-1.a1b2c3d") so
    // server logs identify which build called. It lives in an assembly-level
    // attribute, which Native AOT can trim — self-test asserts it still resolves.
    internal static readonly string ClientVersion =
        typeof(GrimoireApiClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(GrimoireApiClient).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    public static void CheckServerVersion(string? version)
    {
        if (string.IsNullOrEmpty(version)) return;

        if (CompareVersions(version, MinSupportedVersion) < 0)
            _logger.Warn($"Grimoire server version {version} is older than the minimum supported version ({MinSupportedVersion}). Some features may not work.");
        else if (CompareVersions(version, MaxTestedVersion) > 0)
            _logger.Warn($"Grimoire server version {version} has not been tested with this version of grimoire-cli. Proceed with caution.");
        else
            _logger.Debug($"server version {version} (in tested range {MinSupportedVersion}-{MaxTestedVersion})");
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
