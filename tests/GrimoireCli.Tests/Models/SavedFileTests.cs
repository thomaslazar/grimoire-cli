using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class SavedFileTests
{
    [Fact]
    public void SavedFileSerialisesTheWireNames()
    {
        var json = JsonSerializer.Serialize(
            new SavedFile { Path = "/tmp/cover.png", Bytes = 4096 },
            AppJsonContext.Default.SavedFile);
        Assert.Contains("\"path\":", json);
        Assert.Contains("\"bytes\":", json);
    }
}
