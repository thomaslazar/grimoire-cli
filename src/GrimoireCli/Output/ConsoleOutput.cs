using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
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

    public static void WriteRawJson(string json)
    {
        Console.Out.WriteLine(json);
    }

    /// <summary>
    /// Writes a binary body. "-" sends the bytes to stdout and prints nothing
    /// else; any other value is a file path, written and then reported as JSON so
    /// stdout stays parseable in the default case. The stdout parameter exists so
    /// the "-" branch is testable.
    /// </summary>
    public static async Task WriteStreamAsync(Stream source, string output, Stream? stdout = null)
    {
        if (output == "-")
        {
            await using var target = stdout ?? Console.OpenStandardOutput();
            await source.CopyToAsync(target);
            return;
        }
        long bytes;
        await using (var file = new FileStream(output, FileMode.Create, FileAccess.Write))
        {
            await source.CopyToAsync(file);
            bytes = file.Length;
        }
        WriteJson(new Models.SavedFile { Path = output, Bytes = bytes }, Models.AppJsonContext.Default.SavedFile);
    }
}
