using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Kiota.Abstractions.Serialization;

namespace GrimoireCli.Tools.GenerateJsonExamples;

/// <summary>
/// Renders a Kiota request model as a pretty-printed JSON body template.
/// Field names come from <c>GetFieldDeserializers()</c> — the set
/// <c>JsonBodyInput.Validate</c> accepts — and values are placeholders typed
/// from the matching property.
/// </summary>
public static class KiotaSampleWalker
{
    // A backstop for models that nest deeply without recursing. Recursion itself
    // is detected by the ancestor path and rendered as a placeholder, so hitting
    // this means a genuinely deep model, not a cycle.
    private const int MaxDepth = 8;

    // UnsafeRelaxedJsonEscaping keeps '<' and '>' unescaped so "<string>"
    // renders literally in help output.
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Render(Type type)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            WriteModel(writer, type, depth: 0, path: new HashSet<Type>());
        }
        // Utf8JsonWriter indents with Environment.NewLine, so normalise to LF or
        // raw \r bytes leak into the generated string literals on Windows.
        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n");
    }

    private static void WriteModel(Utf8JsonWriter writer, Type type, int depth, HashSet<Type> path)
    {
        // A model that contains itself — a tree node, say — cannot be expanded to
        // a finite sample, so the repeat renders as a placeholder naming the type.
        if (!path.Add(type))
        {
            writer.WriteStringValue($"<{type.Name}>");
            return;
        }
        if (depth > MaxDepth)
            throw new InvalidOperationException(
                $"Model nesting passed {MaxDepth} levels at '{type.Name}' without recursing. " +
                "Raise MaxDepth if the model is genuinely that deep.");
        var instance = (IParsable)Activator.CreateInstance(type)!;
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        writer.WriteStartObject();
        foreach (var wireName in instance.GetFieldDeserializers().Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var property = Resolve(properties, wireName)
                ?? throw new InvalidOperationException(
                    $"'{type.Name}' deserializes the wire field '{wireName}' but exposes no matching property, " +
                    "so the help text and JsonBodyInput.Validate would disagree about it.");
            writer.WritePropertyName(wireName);
            WriteValue(writer, property.PropertyType, depth, path);
        }
        writer.WriteEndObject();
        path.Remove(type);
    }

    /// <summary>Maps a snake_case wire name onto Kiota's PascalCase property.</summary>
    private static PropertyInfo? Resolve(PropertyInfo[] properties, string wireName)
    {
        var target = wireName.Replace("_", "");
        return properties.FirstOrDefault(p => string.Equals(p.Name, target, StringComparison.OrdinalIgnoreCase));
    }

    private static void WriteValue(Utf8JsonWriter writer, Type type, int depth, HashSet<Type> path)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null)
        {
            WriteValue(writer, underlying, depth, path);
            return;
        }
        if (type == typeof(string)) { writer.WriteStringValue("<string>"); return; }
        if (type == typeof(bool)) { writer.WriteBooleanValue(false); return; }
        // Kiota maps `format: date-time` to DateTimeOffset. The placeholder names
        // the wire format rather than a date, because a real timestamp would read
        // as a value to copy.
        if (type == typeof(DateTimeOffset)) { writer.WriteStringValue("<iso8601>"); return; }
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) ||
            type == typeof(double) || type == typeof(float) || type == typeof(decimal))
        {
            writer.WriteNumberValue(0);
            return;
        }
        // Kiota tags each enum member [EnumMember] with its wire string. A
        // placeholder must be a suggestion of what to send, not one real value
        // standing in for the rest, so every member's wire string is joined
        // into a single "<a|b|c>" placeholder.
        if (type.IsEnum)
        {
            var wireValues = Enum.GetValues(type).Cast<object>()
                .Select(member => type.GetField(member.ToString()!)?.GetCustomAttribute<EnumMemberAttribute>()?.Value
                    ?? member.ToString()!);
            writer.WriteStringValue($"<{string.Join('|', wireValues)}>");
            return;
        }
        // UntypedNode is free-form JSON the spec gives no shape for, and it also
        // implements IParsable, so it must be answered before the model branch.
        if (type == typeof(UntypedNode))
        {
            writer.WriteStartObject();
            writer.WriteEndObject();
            return;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            writer.WriteStartArray();
            WriteValue(writer, type.GetGenericArguments()[0], depth, path);
            writer.WriteEndArray();
            return;
        }
        if (typeof(IComposedTypeWrapper).IsAssignableFrom(type))
        {
            var branches = ValueBranches(type);
            // A genuine union — a heterogeneous item list, not FastAPI's
            // anyOf: [T, null]. No branch is canonical, so naming them all beats
            // promoting one arbitrarily, and the enum rule's "<a|b|c>" is already
            // the convention for "one of these goes here".
            if (branches.Count > 1)
            {
                writer.WriteStringValue($"<{string.Join('|', branches.Select(b => b.Name))}>");
                return;
            }
            WriteValue(writer, branches[0], depth, path);
            return;
        }
        if (typeof(IParsable).IsAssignableFrom(type))
        {
            WriteModel(writer, type, depth + 1, path);
            return;
        }
        throw new NotSupportedException(
            $"KiotaSampleWalker has no rule for '{type}'. Add one rather than letting the sample omit the field.");
    }

    /// <summary>
    /// Picks the value branches out of a composed type. FastAPI declares optional
    /// fields as <c>anyOf: [T, null]</c>, which Kiota emits as a wrapper holding
    /// T beside an empty <c>…Member1</c> model standing for the null branch — so
    /// one branch is the common case. More than one is a real union.
    /// </summary>
    private static List<Type> ValueBranches(Type wrapper)
    {
        var properties = wrapper.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "AdditionalData")
            .ToList();
        foreach (var discarded in properties.Where(p => p.Name.EndsWith("Member1", StringComparison.Ordinal)))
        {
            var instance = (IParsable)Activator.CreateInstance(discarded.PropertyType)!;
            if (instance.GetFieldDeserializers().Count != 0)
                throw new NotSupportedException(
                    $"'{discarded.PropertyType.Name}' is discarded as '{wrapper.Name}''s null branch but has " +
                    "fields of its own; the unwrap rule assumes the discarded branch is always empty.");
        }
        var branches = properties
            .Where(p => !p.Name.EndsWith("Member1", StringComparison.Ordinal))
            .Select(p => p.PropertyType)
            .ToList();
        if (branches.Count == 0)
            throw new NotSupportedException(
                $"Composed type '{wrapper.Name}' has no value branch; the unwrap rule assumes at least one.");
        return branches;
    }
}
