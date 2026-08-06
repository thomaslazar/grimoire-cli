namespace GrimoireCli.Api;

// Paths are taken from the live instance's /api/openapi.json (v1.5.4), a snapshot of
// which is committed at docs/grimoire-openapi-1.5.4.json. Only the endpoints the CLI
// currently uses are listed; add rows as commands land rather than transcribing all 130.
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
