namespace GrimoireCli.Api;

// Paths are taken from a running instance's /api/openapi.json (v1.5.4). No snapshot is
// committed — it would go stale; fetch a fresh one into temp/ instead (see CLAUDE.md).
// Only the endpoints the CLI currently uses are listed; add rows as commands land
// rather than transcribing all 130.
public static class ApiEndpoints
{
    // Auth. There is no refresh endpoint — the JWT is valid 30 days and expiry means
    // logging in again.
    public const string Login = "api/auth/login";
    public const string Me = "api/auth/me";
    public const string About = "api/about";

    public const string Systems = "api/systems";
    public static string System(string id) => $"api/systems/{id}";

    public const string Books = "api/books";
    public static string Book(string id) => $"api/books/{id}";

    public const string Rescan = "api/rescan";
    public const string ScanStatus = "api/scan-status";
}
