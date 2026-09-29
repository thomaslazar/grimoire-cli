using GrimoireCli.Services;

namespace GrimoireCli.Tests.Services;

public class CampaignFilesServiceTests
{
    // The server sets is_image from this type (uploads.py:589).
    [Theory]
    [InlineData("map.PNG", "image/png")]
    [InlineData("a.jpeg", "image/jpeg")]
    [InlineData("a.webp", "image/webp")]
    [InlineData("a.gif", "image/gif")]
    [InlineData("handout.pdf", "application/pdf")]
    [InlineData("scene.uvtt", "application/octet-stream")]
    public void MimeForExtension(string path, string expected)
        => Assert.Equal(expected, CampaignFilesService.MimeForExtension(path));
}
