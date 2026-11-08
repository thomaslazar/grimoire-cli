using System.Text;
using GrimoireCli.Api;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace GrimoireCli.Tests.Api;

public class StreamedMultipartBodyTests
{
    private static readonly byte[] FileBytes = Encoding.UTF8.GetBytes("%PDF-1.7 not really a pdf");

    private static StreamedMultipartBody Body(string fileName = "Grüße.pdf")
        => new(FilesService.UploadFields("books/Dsa", "Core", "replace"), fileName, "application/octet-stream", new MemoryStream(FileBytes));

    private static byte[] ReadAll(Stream stream, int chunk)
    {
        var output = new MemoryStream();
        var buffer = new byte[chunk];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            output.Write(buffer, 0, read);
        return output.ToArray();
    }

    // The upload used to go through Kiota's MultipartBody; the server must keep
    // seeing the same bytes, boundary aside.
    [Fact]
    public void BytesMatchKiotaMultipartBody()
    {
        using var body = Body();
        var kiota = new MultipartBody();
        kiota.AddOrReplacePart("destination", "text/plain", "books/Dsa");
        kiota.AddOrReplacePart("relative_dir", "text/plain", "Core");
        kiota.AddOrReplacePart("on_conflict", "text/plain", "replace");
        kiota.AddOrReplacePart("file", "application/octet-stream", FileBytes, "Grüße.pdf");
        var info = new RequestInformation { HttpMethod = Method.POST, UrlTemplate = "http://localhost/" };
        info.SetContentFromParsable(new Microsoft.Kiota.Bundle.DefaultRequestAdapter(new AnonymousAuthenticationProvider()), "multipart/form-data", kiota);
        var expected = Encoding.UTF8.GetString(ReadAll(info.Content, 4096)).Replace(kiota.Boundary, body.Boundary);

        Assert.Equal(expected, Encoding.UTF8.GetString(ReadAll(body, 4096)));
        Assert.Equal($"multipart/form-data; boundary={body.Boundary}", body.ContentType);
    }

    [Fact]
    public void LengthIsWhatReadingYields()
    {
        using var body = Body();
        Assert.Equal(body.Length, ReadAll(body, 4096).Length);
    }

    // Small reads cross every segment boundary.
    [Fact]
    public void ChunkedReadsMatchOneRead()
    {
        using var whole = Body();
        using var chunked = Body();
        var expected = ReadAll(whole, 4096);
        Assert.Equal(
            Encoding.UTF8.GetString(expected).Replace(whole.Boundary, chunked.Boundary),
            Encoding.UTF8.GetString(ReadAll(chunked, 7)));
    }

    // The token-refresh retry rewinds the body and sends it again.
    [Fact]
    public async Task RewindingReplaysTheSameBytes()
    {
        await using var body = Body();
        var first = ReadAll(body, 4096);
        body.Position = 0;
        var second = new MemoryStream();
        await body.CopyToAsync(second);
        Assert.Equal(first, second.ToArray());
    }
}
