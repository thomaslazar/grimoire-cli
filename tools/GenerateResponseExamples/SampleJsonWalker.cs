using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrimoireCli.Tools.GenerateResponseExamples;

/// <summary>
/// Per-property overrides the walker applies when rendering a type, keyed by
/// (declaring type, C# property name) so an override on a base class also
/// applies to derived types that inherit the property.
/// </summary>
public class PropertyOverrides
{
    public Dictionary<(Type, string), string> StringValues { get; } = new();
    public Dictionary<(Type, string), int> IntValues { get; } = new();
}

/// <summary>
/// Reflects over a type and emits a pretty-printed JSON sample payload whose
/// shape matches what <see cref="JsonSerializer"/> would produce, with synthetic
/// placeholder values. Used at build time to populate help output.
/// </summary>
public static class SampleJsonWalker
{
    // UnsafeRelaxedJsonEscaping keeps '<', '>' and '&' unescaped so placeholders
    // like "<string>" render literally in help output instead of <string>.
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Render(Type type, PropertyOverrides? overrides = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            WriteValue(writer, type, new HashSet<Type>(), overrides);
        }
        // Normalise to LF: Utf8JsonWriter with Indented=true uses Environment.NewLine
        // in .NET 8, so on Windows raw \r bytes would leak into the generated
        // string literals and break the C# compile cross-platform.
        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n");
    }

    private static void WriteValue(Utf8JsonWriter writer, Type type, HashSet<Type> visiting, PropertyOverrides? overrides = null)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null)
        {
            // Nullable<T> (e.g. bool?, int?) — render the T branch. The sample is
            // representative data, not the null state every such field can also
            // take on.
            WriteValue(writer, underlying, visiting, overrides);
            return;
        }

        if (type == typeof(string))
        {
            writer.WriteStringValue("<string>");
            return;
        }
        // A JsonElement property is a value whose type is decided per row by the
        // server (metadata diff rows carry a string, an int, or a list). There is
        // no single shape to render, so the sample says so.
        if (type == typeof(JsonElement))
        {
            writer.WriteStringValue("<any>");
            return;
        }
        if (type == typeof(bool)) { writer.WriteBooleanValue(false); return; }
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) ||
            type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) ||
            type == typeof(byte) || type == typeof(sbyte))
        {
            writer.WriteNumberValue(0);
            return;
        }
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
        {
            writer.WriteNumberValue(0);
            return;
        }

        // Guard: date/time types. STJ would serialise these as ISO-8601 strings,
        // not objects with Year/Month/etc. If a model adds one, the walker must
        // learn how to emit a representative ISO string — failing loudly is
        // better than silently shipping nonsense in help output.
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
            type == typeof(TimeSpan) || type == typeof(DateOnly) ||
            type == typeof(TimeOnly) || type == typeof(Guid))
        {
            throw new NotSupportedException(
                $"SampleJsonWalker encountered unsupported type '{type}'. Extend the walker " +
                $"to emit the correct placeholder (usually an ISO-8601 string).");
        }

        if (type.IsArray)
        {
            writer.WriteStartArray();
            WriteValue(writer, type.GetElementType()!, visiting, overrides);
            writer.WriteEndArray();
            return;
        }

        if (TryGetDictionaryValue(type, out var valueType))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("<key>");
            WriteValue(writer, valueType, visiting, overrides);
            writer.WriteEndObject();
            return;
        }

        if (TryGetEnumerableElement(type, out var elementType))
        {
            writer.WriteStartArray();
            WriteValue(writer, elementType, visiting, overrides);
            writer.WriteEndArray();
            return;
        }

        if (!visiting.Add(type))
        {
            writer.WriteStringValue("<recursive>");
            return;
        }

        writer.WriteStartObject();
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
            var name = prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? prop.Name;
            writer.WritePropertyName(name);

            var key = (prop.DeclaringType!, prop.Name);
            if (overrides != null && overrides.StringValues.TryGetValue(key, out var stringValue))
            {
                writer.WriteStringValue(stringValue);
                continue;
            }
            if (overrides != null && overrides.IntValues.TryGetValue(key, out var intValue))
            {
                writer.WriteNumberValue(intValue);
                continue;
            }

            WriteValue(writer, prop.PropertyType, visiting, overrides);
        }
        writer.WriteEndObject();
        visiting.Remove(type);
    }

    private static bool TryGetEnumerableElement(Type type, out Type elementType)
    {
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(List<>) || def == typeof(IList<>) ||
                def == typeof(IEnumerable<>) || def == typeof(ICollection<>) ||
                def == typeof(IReadOnlyList<>) || def == typeof(IReadOnlyCollection<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }
        }
        elementType = typeof(object);
        return false;
    }

    private static bool TryGetDictionaryValue(Type type, out Type valueType)
    {
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>) ||
                def == typeof(IReadOnlyDictionary<,>))
            {
                valueType = type.GetGenericArguments()[1];
                return true;
            }
        }
        valueType = typeof(object);
        return false;
    }
}
