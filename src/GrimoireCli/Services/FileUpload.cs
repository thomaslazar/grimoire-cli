using GrimoireCli.Api;
using GrimoireCli.Commands;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// What every file upload shares: the file is streamed from disk in a
/// <see cref="StreamedMultipartBody"/>, and sent with
/// <see cref="GrimoireApiClient.SendUploadAsync"/>, which has no timeout.
/// </summary>
internal static class FileUpload
{
    /// <summary>Opens the file to upload; an unreadable one is a body-input error.</summary>
    public static FileStream Open(string filePath)
    {
        try
        {
            return File.OpenRead(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new BodyInputException($"Could not read {filePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// The request a generated builder makes, carrying <paramref name="body"/>.
    /// The builder supplies the URL and method; Kiota will not serialize an
    /// empty MultipartBody, so it gets a one-part placeholder that the streamed
    /// body then replaces whole. SetStreamContent keeps a Content-Type already
    /// set, which would name the placeholder's boundary, so that goes first.
    /// </summary>
    public static RequestInformation Request(Func<MultipartBody, RequestInformation> build, StreamedMultipartBody body)
    {
        var placeholder = new MultipartBody();
        placeholder.AddOrReplacePart("file", "text/plain", "");
        var info = build(placeholder);
        info.Headers.Remove("Content-Type");
        info.SetStreamContent(body, body.ContentType);
        return info;
    }

    /// <summary>The body for a single file part with no form fields.</summary>
    public static StreamedMultipartBody Body(Stream file, string filePath, string contentType)
        => new([], Path.GetFileName(filePath), contentType, file);
}
