using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class CoverDtoTests
{
    [Fact]
    public void CoverUploadResultCarriesTheStoredFilename()
    {
        const string json = """{"cover_image": "8f3c-1d2e.png"}""";
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.CoverUploadResult)!;
        Assert.Equal("8f3c-1d2e.png", result.CoverImage);
    }
}
