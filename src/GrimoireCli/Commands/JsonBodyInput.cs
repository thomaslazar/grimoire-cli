using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace GrimoireCli.Commands;

/// <summary>A client-side refusal, carrying the message to print before exiting 1.</summary>
public class BodyInputException : Exception
{
    public BodyInputException(string message) : base(message) { }
}

/// <summary>
/// Reads a JSON request body from --input or --stdin and checks its shape against a
/// request DTO. The body is validated by deserializing it and is then sent
/// unchanged, so an explicit "" stays "" and an omitted field stays omitted.
/// </summary>
public static class JsonBodyInput
{
    public static string Read(string? inputPath, bool useStdin)
    {
        if (inputPath != null && useStdin)
            throw new BodyInputException("Provide --input or --stdin, not both.");
        if (inputPath == null && !useStdin)
            throw new BodyInputException("A request body is required. Provide --input <file> or --stdin.");

        string body;
        if (useStdin)
        {
            body = Console.In.ReadToEnd();
            if (string.IsNullOrWhiteSpace(body))
                throw new BodyInputException("The request body on stdin is empty.");
        }
        else
        {
            try
            {
                body = File.ReadAllText(inputPath!);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new BodyInputException($"Could not read {inputPath}: {ex.Message}");
            }
            if (string.IsNullOrWhiteSpace(body))
                throw new BodyInputException($"The request body in {inputPath} is empty.");
        }
        return body;
    }

    /// <summary>
    /// Deserializes to check the shape and discards the result. Unknown keys throw
    /// because the request DTOs carry JsonUnmappedMemberHandling.Disallow — Grimoire
    /// itself drops them and answers success, so this is the only place a typo can
    /// still be caught.
    /// </summary>
    public static void Validate<T>(string json, JsonTypeInfo<T> typeInfo, string idHint)
    {
        try
        {
            JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException ex)
        {
            throw new BodyInputException(Translate(ex, typeInfo, idHint));
        }
    }

    private static string Translate<T>(JsonException ex, JsonTypeInfo<T> typeInfo, string idHint)
    {
        var key = UnknownKey(ex);
        if (key == null)
            // Path is null for some parse failures and "$" (the document root, naming
            // no real field) for others — System.Text.Json is not consistent about
            // which. Either way there is no field to point at, so both read as
            // "the document itself is bad" rather than "this field is bad".
            return ex.Path is null or "$"
                ? $"The request body is not valid JSON. {ex.Message}"
                : $"The request body is invalid at {ex.Path}. {ex.Message}";

        var message = $"Unknown field '{key}' in the request body at {ex.Path}.";
        if (key == "id")
            return $"{message} 'id' is not an editable field — {idHint}.";

        var allowed = typeInfo.Properties.Select(p => p.Name).ToList();
        var nearest = allowed
            .Select(name => (name, distance: Distance(key, name)))
            .Where(c => c.distance <= 3)
            .OrderBy(c => c.distance)
            .Select(c => c.name)
            .FirstOrDefault();
        return nearest != null
            ? $"{message} Did you mean '{nearest}'?"
            : $"{message} Allowed fields: {string.Join(", ", allowed)}.";
    }

    // The unmapped-member message is the only one whose Path's last segment is a
    // key that does not exist on the type; a type mismatch names a real field and
    // must not be offered a suggestion.
    private static string? UnknownKey(JsonException ex)
    {
        if (ex.Path == null) return null;
        if (!ex.Message.Contains("could not be mapped", StringComparison.Ordinal)) return null;
        var lastDot = ex.Path.LastIndexOf('.');
        return lastDot < 0 ? null : ex.Path[(lastDot + 1)..];
    }

    // Levenshtein, iterative with two rows. Only ever runs on one rejected key
    // against at most 18 field names, so nothing here needs to be clever.
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
