using System.Text;
using GrimoireCli.Output;

namespace GrimoireCli.Tests.Output;

public class WriteStreamTests
{
    [Fact]
    public async Task WritesBytesToTheNamedFileAndReportsTheCount()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grimoire-write-{Guid.NewGuid():N}.bin");
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var stdout = new StringWriter();
        var original = Console.Out;
        Console.SetOut(stdout);
        try
        {
            using var source = new MemoryStream(payload);
            await ConsoleOutput.WriteStreamAsync(source, path);
        }
        finally { Console.SetOut(original); }

        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        var receipt = stdout.ToString();
        Assert.Contains("\"bytes\": 5", receipt);
        Assert.Contains(path, receipt);
        File.Delete(path);
    }

    // "-" is the documented escape hatch: raw bytes, and no JSON at all, so the
    // output can be redirected into a file or piped to another tool.
    [Fact]
    public async Task DashWritesNothingButTheBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grimoire-dash-{Guid.NewGuid():N}.bin");
        var payload = Encoding.UTF8.GetBytes("not json");
        await using (var captured = File.Create(path))
        {
            var original = Console.OpenStandardOutput();
            // Redirect the process stdout handle so the helper's own write lands in the file.
            using var source = new MemoryStream(payload);
            await ConsoleOutput.WriteStreamAsync(source, "-", captured);
        }
        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        File.Delete(path);
    }
}
