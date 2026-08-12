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

            var node = new JsonParseNode(document.RootElement);

            // The root's keys are checked without parsing any value, so this cannot
            // be skipped by a value the parser chokes on — and a misspelled field at
            // the top level is the case that matters most.
            var rootAllowed = factory(node).GetFieldDeserializers().Keys;
            var unknown = document.RootElement.EnumerateObject()
                .Where(property => !rootAllowed.Contains(property.Name))
                .Select(property => (Key: property.Name, Allowed: (IEnumerable<string>)rootAllowed, AtRoot: true))
                .ToList();
            if (unknown.Count > 0)
                throw new BodyInputException(Describe(unknown, json, idHint));

            // Nested objects need the values parsed. Kiota routes any key a model does
            // not declare into its AdditionalData rather than failing, and propagates
            // this callback into every object it parses, so one hook reaches every
            // depth — each key paired with the model that rejected it.
            node.OnAfterAssignFieldValues = parsed =>
            {
                if (parsed is not IAdditionalDataHolder holder || holder.AdditionalData.Count == 0) return;
                var allowed = parsed.GetFieldDeserializers().Keys;
                foreach (var key in holder.AdditionalData.Keys)
                    unknown.Add((key, allowed, false));
            };
            try
            {
                node.GetObjectValue(factory);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                // Kiota's getters tolerate most wrong types, returning null rather than
                // throwing; a number too large for its target is the case that does throw,
                // and the server accepts some of those, so silence here would let a typo
                // elsewhere in the body through unexamined. Report what was found and say
                // plainly that the rest went unchecked.
                _logger.Debug($"request body did not parse against the generated model: {ex.Message}");
                if (unknown.Count > 0)
                    throw new BodyInputException(Describe(unknown, json, idHint));
                _logger.Warn("A value in the body stopped the field check early. The top-level fields "
                             + "were checked and any nested object reached before it was too. Run with "
                             + "--debug for the parse error.");
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

    private static string Describe(
        List<(string Key, IEnumerable<string> Allowed, bool AtRoot)> unknown, string json, string idHint)
    {
        var (key, allowed, atRoot) = unknown[0];
        // A root offender's location is known outright, so it survives a name that
        // also appears — legally — deeper in the body. Only a nested one has to be
        // found by name, and only then can the match be ambiguous.
        var found = atRoot ? $"$.{key}" : PathOf(json, key);
        var location = found is null ? "" : $" at {found}";
        var message = $"Unknown field '{key}' in the request body{location}.";
        if (unknown.Count > 1)
            message += $" ({unknown.Count - 1} more: {string.Join(", ", unknown.Skip(1).Select(u => $"'{u.Key}'"))})";
        // Only the root's id belongs elsewhere; inside a batch item an id is required,
        // and inside a nested entry it is just another unknown field.
        if (key == "id" && atRoot)
            return $"{message} 'id' is not an editable field — {idHint}.";

        var candidates = allowed.ToList();
        var nearest = candidates
            .Select(name => (name, distance: Distance(key, name)))
            .Where(c => c.distance <= SuggestionThreshold(key))
            .OrderBy(c => c.distance)
            .Select(c => c.name)
            .FirstOrDefault();
        return nearest != null
            ? $"{message} Did you mean '{nearest}'?"
            : $"{message} Allowed fields: {string.Join(", ", candidates)}.";
    }

    // A fixed edit distance is far too generous on short keys: at 3, "a" reaches
    // "name" and every four-letter field is everyone's neighbour. Scale it instead,
    // so a suggestion means the key was nearly right rather than merely short.
    private static int SuggestionThreshold(string key) => key.Length switch
    {
        <= 4 => 1,
        <= 7 => 2,
        _ => 3
    };

    /// <summary>
    /// Locates a property by name so the message can point at it. The callback that
    /// reports an unknown key knows the model that rejected it but not where in the
    /// document it sat, so the name is matched against the raw body — and only a name
    /// occurring exactly once can be placed that way. When it appears more than once,
    /// the offender is ambiguous: naming the wrong one would send a caller to rename a
    /// field that is perfectly legal where it sits.
    /// </summary>
    private static string? PathOf(string json, string key)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var found = new List<string>();
            Search(document.RootElement, key, "$", found);
            return found.Count == 1 ? found[0] : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Search(JsonElement element, string key, string path, List<string> found)
    {
        if (found.Count > 1) return;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == key) found.Add($"{path}.{key}");
                Search(property.Value, key, $"{path}.{property.Name}", found);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                Search(item, key, $"{path}[{index}]", found);
                index++;
            }
        }
    }

    // Optimal string alignment: Levenshtein plus adjacent transposition at cost 1,
    // so "nmae" is one edit from "name" rather than two. Typing two letters in the
    // wrong order is the common miss, and on a four-letter field name it is the only
    // one a threshold tight enough to be trustworthy can still afford to catch.
    // Runs once per rejected key against at most 18 names, so a full matrix is fine.
    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }
        return d[a.Length, b.Length];
    }
}
