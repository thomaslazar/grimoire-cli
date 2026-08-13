using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Kiota.Abstractions.Serialization;

namespace GrimoireCli.Tools.GenerateRequestExamples;

/// <summary>
/// Renders a Kiota request model as a pretty-printed JSON body template.
/// Field names come from <c>GetFieldDeserializers()</c> — the set
/// <c>JsonBodyInput.Validate</c> accepts — and values are placeholders typed
/// from the matching property.
/// </summary>
public static class KiotaSampleWalker
{
    // A model nested this deep is a recursive reference the placeholder scheme
    // has no answer for; nothing in the tree reaches it today.
    private const int MaxDepth = 5;

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
            WriteModel(writer, type, depth: 0);
        }
        // Utf8JsonWriter indents with Environment.NewLine, so normalise to LF or
        // raw \r bytes leak into the generated string literals on Windows.
        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n");
    }

    private static void WriteModel(Utf8JsonWriter writer, Type type, int depth)
    {
        if (depth > MaxDepth)
            throw new InvalidOperationException(
                $"Model nesting passed {MaxDepth} levels at '{type.Name}'. A recursive model needs an explicit placeholder.");
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
            WriteValue(writer, property.PropertyType, depth);
        }
        writer.WriteEndObject();
    }

    /// <summary>Maps a snake_case wire name onto Kiota's PascalCase property.</summary>
    private static PropertyInfo? Resolve(PropertyInfo[] properties, string wireName)
    {
        var target = wireName.Replace("_", "");
        return properties.FirstOrDefault(p => string.Equals(p.Name, target, StringComparison.OrdinalIgnoreCase));
    }

    private static void WriteValue(Utf8JsonWriter writer, Type type, int depth)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null)
        {
            WriteValue(writer, underlying, depth);
            return;
        }
        if (type == typeof(string)) { writer.WriteStringValue("<string>"); return; }
        if (type == typeof(bool)) { writer.WriteBooleanValue(false); return; }
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
            WriteValue(writer, type.GetGenericArguments()[0], depth);
            writer.WriteEndArray();
            return;
        }
        if (typeof(IComposedTypeWrapper).IsAssignableFrom(type))
        {
            WriteValue(writer, ValueBranch(type), depth);
            return;
        }
        if (typeof(IParsable).IsAssignableFrom(type))
        {
            WriteModel(writer, type, depth + 1);
            return;
        }
        throw new NotSupportedException(
            $"KiotaSampleWalker has no rule for '{type}'. Add one rather than letting the sample omit the field.");
    }

    /// <summary>
    /// Picks the value branch out of a composed type. FastAPI declares optional
    /// fields as <c>anyOf: [T, null]</c>, which Kiota emits as a wrapper holding
    /// T beside an empty <c>…Member1</c> model standing for the null branch.
    /// </summary>
    private static Type ValueBranch(Type wrapper)
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
            .ToList();
        if (branches.Count != 1)
            throw new NotSupportedException(
                $"Composed type '{wrapper.Name}' has {branches.Count} value branches; the unwrap rule assumes exactly one.");
        return branches[0].PropertyType;
    }
}
