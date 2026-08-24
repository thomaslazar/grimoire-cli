using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GrimoireCli.Commands;
using GrimoireCli.Models;

namespace GrimoireCli.Output;

public static class ConsoleOutput
{
    public static void WriteJson(Dictionary<string, string> data)
    {
        var json = JsonSerializer.Serialize(data, AppJsonContext.Default.DictionaryStringString);
        Console.Out.WriteLine(json);
    }

    public static void WriteJson<T>(T data, JsonTypeInfo<T> typeInfo)
    {
        var json = JsonSerializer.Serialize(data, typeInfo);
        Console.Out.WriteLine(json);
    }

    /// <summary>
    /// Set from the root --pretty option. Off means the server's bytes reach
    /// stdout unchanged, which keeps undeclared fields, explicit nulls and the
    /// server's key order.
    /// </summary>
    public static bool Pretty { get; set; }

    /// <summary>
    /// Writes a response body. Verbatim by default; re-indented under --pretty,
    /// which costs a parse and re-serialize, so the bytes are no longer the
    /// server's. A body that does not parse is passed through untouched rather
    /// than swallowed — GrimoireApiClient.EnsureJson is what rejects those, and
    /// this must not become a second, quieter place that decides.
    /// </summary>
    public static void WriteRawJson(string json)
    {
        if (!Pretty)
        {
            Console.Out.WriteLine(json);
            return;
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            // JsonElement.WriteTo needs no reflection, unlike JsonSerializer.Serialize(JsonElement),
            // which is unconditionally annotated RequiresUnreferencedCode/RequiresDynamicCode even
            // though JsonElement itself needs neither — this keeps --pretty AOT-warning-free.
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                doc.RootElement.WriteTo(writer);
            Console.Out.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
        }
        catch (JsonException)
        {
            Console.Out.WriteLine(json);
        }
    }

    /// <summary>
    /// Writes a binary body. "-" sends the bytes to stdout and prints nothing
    /// else; any other value is a file path, written and then reported as JSON so
    /// stdout stays parseable in the default case. The stdout parameter exists so
    /// the "-" branch is testable; when supplied, the caller owns it and it is not
    /// disposed here.
    /// </summary>
    public static async Task WriteStreamAsync(Stream source, string output, Stream? stdout = null)
    {
        if (output == "-")
        {
            var target = stdout ?? Console.OpenStandardOutput();
            try
            {
                await source.CopyToAsync(target);
            }
            finally
            {
                if (stdout is null) await target.DisposeAsync();
            }
            return;
        }
        FileStream file;
        try
        {
            file = new FileStream(output, FileMode.Create, FileAccess.Write);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new BodyInputException($"Could not write {output}: {ex.Message}");
        }
        long bytes;
        await using (file)
        {
            await source.CopyToAsync(file);
            bytes = file.Length;
        }
        WriteJson(new Models.SavedFile { Path = output, Bytes = bytes }, Models.AppJsonContext.Default.SavedFile);
    }
}
