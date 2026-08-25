using System.Text;
using GrimoireCli.Commands;
using GrimoireCli.Output;

namespace GrimoireCli.Tests.Output;

[Collection("Console")]
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

            Assert.Equal(payload, await File.ReadAllBytesAsync(path));
            var receipt = stdout.ToString();
            Assert.Contains("\"bytes\": 5", receipt);
            Assert.Contains(path, receipt);
        }
        finally
        {
            Console.SetOut(original);
            File.Delete(path);
        }
    }

    // "-" is the documented escape hatch: raw bytes, and no JSON at all, so the
    // output can be redirected into a file or piped to another tool.
    [Fact]
    public async Task DashWritesNothingButTheBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grimoire-dash-{Guid.NewGuid():N}.bin");
        var payload = Encoding.UTF8.GetBytes("not json");
        try
        {
            await using (var captured = File.Create(path))
            {
                // The explicit stdout argument, not a redirected Console.Out, is what
                // routes the helper's write into the file.
                using var source = new MemoryStream(payload);
                await ConsoleOutput.WriteStreamAsync(source, "-", captured);
            }
            Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }

    // Reproduces the unguarded `new FileStream(...)`: an unwritable --output
    // directory must report BodyInputException the same way JsonBodyInput.Read
    // does for a missing --input, not throw raw after the download already
    // happened.
    [Fact]
    public async Task ReportsAnUnwritableOutputDirectoryInsteadOfThrowingRaw()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Path.Combine(Path.GetTempPath(), $"grimoire-unwritable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var path = Path.Combine(dir, "out.bin");
        try
        {
            using var source = new MemoryStream(new byte[] { 1, 2, 3 });
            var ex = await Assert.ThrowsAsync<BodyInputException>(() => ConsoleOutput.WriteStreamAsync(source, path));
            Assert.Contains(path, ex.Message);
        }
        finally
        {
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(dir, recursive: true);
        }
    }
}
