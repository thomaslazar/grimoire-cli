using System.Text;
using System.Text.Json;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;

namespace GrimoireCli.Commands;

/// <summary>A client-side refusal, carrying the message to print before exiting 1.</summary>
public class BodyInputException : Exception
{
    public BodyInputException(string message) : base(message) { }
}

/// <summary>
/// Reads a JSON request body from --input or --stdin and checks its field names
/// against the generated model for that endpoint. The body is validated and then
/// sent unchanged, so an explicit "" stays "" and an omitted field stays omitted.
/// </summary>
public static class JsonBodyInput
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    // Shared with SystemsCommand.RequireExactlyOneBodySource, which enforces the
    // same both/neither rule as a parse validator so the CLI's own refusal fires
    // first. This copy stays reachable only through direct unit tests.
    internal const string BothSourcesMessage = "Provide --input or --stdin, not both.";
    internal const string NeitherSourceMessage = "A request body is required. Provide --input <file> or --stdin.";

    public static string Read(string? inputPath, bool useStdin, TextReader? stdin = null)
    {
        if (inputPath != null && useStdin)
            throw new BodyInputException(BothSourcesMessage);
        if (inputPath == null && !useStdin)
            throw new BodyInputException(NeitherSourceMessage);

        string body;
        if (useStdin)
        {
            // Console.In decodes with Console.InputEncoding, which on Windows is
            // the console code page (commonly 437/1252), not UTF-8. Read the raw
            // stream ourselves so non-ASCII bodies survive on every platform.
            var reader = stdin ?? new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            body = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(body))
                throw new BodyInputException("The request body on stdin is empty.");
        }
        else
        {
            try
            {
                body = File.ReadAllText(inputPath!);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw new BodyInputException($"Could not read {inputPath}: {ex.Message}");
            }
            if (string.IsNullOrWhiteSpace(body))
                throw new BodyInputException($"The request body in {inputPath} is empty.");
        }
        return body;
    }

    /// <summary>
    /// Rejects a body carrying a field the endpoint does not define, at any depth.
    /// Grimoire drops unknown keys at validation and still answers success, so this
    /// is the only place a misspelled field can be caught at all.
    /// </summary>
    /// <param name="json">The raw body, which is sent unchanged once it passes.</param>
    /// <param name="factory">
    /// The generated model's <c>CreateFromDiscriminatorValue</c>. The field list comes
    /// from the generator, so it cannot drift from the API: there is no hand-written
    /// copy of it to maintain.
    /// </param>
    /// <param name="idHint">Where an <c>id</c> belongs, for the endpoints that take one elsewhere.</param>
    public static void Validate<T>(string json, ParsableFactory<T> factory, string idHint) where T : IParsable
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new BodyInputException($"The request body is not valid JSON. {ex.Message}");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new BodyInputException(
                    $"The request body must be a JSON object, not {Describe(document.RootElement.ValueKind)}.");

            // Kiota routes any key a model does not declare into its AdditionalData
            // rather than failing, and propagates this callback into every nested
            // object it parses — so one hook catches unknown fields at every depth,
            // each paired with the model that rejected it.
            var unknown = new List<(string Key, IEnumerable<string> Allowed)>();
            var node = new JsonParseNode(document.RootElement)
            {
                OnAfterAssignFieldValues = parsed =>
                {
                    if (parsed is not IAdditionalDataHolder holder || holder.AdditionalData.Count == 0) return;
                    var allowed = parsed.GetFieldDeserializers().Keys;
                    foreach (var key in holder.AdditionalData.Keys)
                        unknown.Add((key, allowed));
                }
            };
            try
            {
                node.GetObjectValue(factory);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                // A value of the wrong type lands here. The server reports those as a
                // 422 rather than dropping them, so the body is let through with the
                // parse failure recorded for --debug rather than refused on a guess
                // about which value the model choked on.
                _logger.Debug($"request body did not parse against the generated model: {ex.Message}");
                return;
            }
            if (unknown.Count > 0)
                throw new BodyInputException(Describe(unknown, json, idHint));
        }
    }

    private static string Describe(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Null => "null",
        _ => "a value"
    };

    private static string Describe(List<(string Key, IEnumerable<string> Allowed)> unknown, string json, string idHint)
    {
        var (key, allowed) = unknown[0];
        var location = PathOf(json, key) is { } path ? $" at {path}" : "";
        var message = $"Unknown field '{key}' in the request body{location}.";
        if (unknown.Count > 1)
            message += $" ({unknown.Count - 1} more: {string.Join(", ", unknown.Skip(1).Select(u => $"'{u.Key}'"))})";
        if (key == "id")
            return $"{message} 'id' is not an editable field — {idHint}.";

        var candidates = allowed.ToList();
        var nearest = candidates
            .Select(name => (name, distance: Distance(key, name)))
            .Where(c => c.distance <= 3)
            .OrderBy(c => c.distance)
            .Select(c => c.name)
            .FirstOrDefault();
        return nearest != null
            ? $"{message} Did you mean '{nearest}'?"
            : $"{message} Allowed fields: {string.Join(", ", candidates)}.";
    }

    /// <summary>
    /// Locates a property by name so the message can point at it. The callback that
    /// reports an unknown key knows the model that rejected it but not where in the
    /// document it sat; the first match is enough to steer a caller to the right
    /// entry of a batch body.
    /// </summary>
    private static string? PathOf(string json, string key)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return Search(document.RootElement, key, "$");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Search(JsonElement element, string key, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == key) return $"{path}.{key}";
                if (Search(property.Value, key, $"{path}.{property.Name}") is { } found) return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                if (Search(item, key, $"{path}[{index}]") is { } found) return found;
                index++;
            }
        }
        return null;
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
