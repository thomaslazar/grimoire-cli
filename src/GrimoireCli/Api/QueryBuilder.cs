namespace GrimoireCli.Api;

/// <summary>
/// Builds the query string for a request. Unset parameters are omitted entirely
/// rather than sent empty, because Grimoire treats an empty filter as a filter.
/// </summary>
public static class QueryBuilder
{
    public static string Build(params (string Name, string? Value)[] parameters)
    {
        var parts = parameters
            .Where(p => !string.IsNullOrEmpty(p.Value))
            .Select(p => $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(p.Value!)}")
            .ToArray();
        return parts.Length == 0 ? "" : "?" + string.Join("&", parts);
    }
}
