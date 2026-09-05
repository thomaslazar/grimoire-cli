using GrimoireCli.Api;
using GrimoireCli.Commands;

namespace GrimoireCli.Services;

/// <summary>
/// The ten library file-management endpoints, every one require_admin. They
/// write inside the library tree, so each one answers 409 when the library is
/// mounted read-only — Grimoire detects that from EROFS on the write itself
/// (services/library_fs/folders.py).
/// </summary>
public class FilesService
{
    private const string AdminHint = "the admin role";
    private const string NotFoundHint =
        "No such path in the library. List a folder with: grimoire-cli files browse --path <path>";

    private readonly GrimoireApiClient _client;

    public FilesService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/files/browse. Merged with the index, and capped at 2000 entries.</summary>
    public async Task<string> BrowseAsync(string? path, int? limit)
    {
        var info = _client.Api.Api.Files.Browse.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Path = path;
            c.QueryParameters.Limit = limit;
        });
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// POST /api/files/upload. One file per request by the server's design, so
    /// this sends exactly one. The multipart body carries the Form fields
    /// alongside the file part, which FastAPI binds by name.
    /// </summary>
    public async Task<string> UploadAsync(string destination, string filePath, string? relativeDir, string? onConflict)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new BodyInputException($"Could not read {filePath}: {ex.Message}");
        }
        var body = new Microsoft.Kiota.Abstractions.MultipartBody();
        body.AddOrReplacePart("destination", "text/plain", destination);
        if (relativeDir is not null)
            body.AddOrReplacePart("relative_dir", "text/plain", relativeDir);
        if (onConflict is not null)
            body.AddOrReplacePart("on_conflict", "text/plain", onConflict);
        body.AddOrReplacePart("file", "application/octet-stream", bytes, Path.GetFileName(filePath));
        var info = _client.Api.Api.Files.Upload.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/files/move. One request carrying every source.</summary>
    public async Task<string> MoveAsync(string[] sources, string destination, string? onConflict)
    {
        var body = new Generated.Models.MoveRequest
        {
            Sources = [.. sources],
            Destination = destination,
            OnConflict = onConflict,
        };
        var info = _client.Api.Api.Files.Move.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/files/rename.</summary>
    public async Task<string> RenameAsync(string path, string newName)
    {
        var body = new Generated.Models.RenameRequest { Path = path, NewName = newName };
        var info = _client.Api.Api.Files.Rename.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// POST /api/files/delete. Soft unless deleteFiles is set: the rows go and
    /// the files stay, which a rescan then re-adds.
    /// </summary>
    public async Task<string> DeleteAsync(string path, string? confirmName, bool deleteFiles)
    {
        var info = _client.Api.Api.Files.DeletePath.ToPostRequestInformation(
            BuildDeleteBody(path, confirmName, deleteFiles));
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/files/folder.</summary>
    public async Task<string> CreateFolderAsync(string parent, string name, string? containerKind, bool nsfw)
    {
        var body = new Generated.Models.CreateFolderRequest
        {
            Parent = parent,
            Name = name,
            ContainerKind = containerKind,
            Nsfw = nsfw,
        };
        var info = _client.Api.Api.Files.Folder.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// DELETE /api/files/folder, which carries a request body. Always removes the
    /// files: unlike files delete, it has no soft form.
    /// </summary>
    public async Task<string> DeleteFolderAsync(string path, string? confirmName)
    {
        var info = _client.Api.Api.Files.Folder.ToDeleteRequestInformation(
            BuildDeleteFolderBody(path, confirmName));
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>PUT /api/files/folder/markers. A partial patch: omitted fields are left alone.</summary>
    public async Task<string> MarkersAsync(string path, string? containerKind, bool? nsfw)
    {
        var info = _client.Api.Api.Files.Folder.Markers.ToPutRequestInformation(
            BuildMarkersBody(path, containerKind, nsfw));
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/files/folder/scaffold. Reports created and existing, so it is idempotent.</summary>
    public async Task<string> ScaffoldAsync(string path)
    {
        var body = new Generated.Models.ScaffoldRequest { Path = path };
        var info = _client.Api.Api.Files.Folder.Scaffold.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>GET /api/files/folder/contents.</summary>
    public async Task<string> FolderContentsAsync(string path)
    {
        var info = _client.Api.Api.Files.Folder.Contents.ToGetRequestInformation(c =>
            c.QueryParameters.Path = path);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// container_kind and nsfw are composed-type wrappers because both are
    /// Optional upstream. Assigning through the wrapper only when the flag was
    /// given is what keeps this a partial patch. Internal (not private) so a test
    /// can pin that a client regeneration cannot silently change it.
    /// </summary>
    internal static Generated.Models.MarkersRequest BuildMarkersBody(string path, string? containerKind, bool? nsfw)
    {
        var body = new Generated.Models.MarkersRequest { Path = path };
        if (containerKind is not null)
            body.ContainerKind = new Generated.Models.MarkersRequest.MarkersRequest_container_kind { String = containerKind };
        if (nsfw is not null)
            body.Nsfw = new Generated.Models.MarkersRequest.MarkersRequest_nsfw { Boolean = nsfw.Value };
        return body;
    }

    /// <summary>
    /// confirm_name is a composed-type wrapper; delete_files is a plain bool the
    /// server defaults to false, and the CLI sends what the flag says.
    /// </summary>
    internal static Generated.Models.DeleteRequest BuildDeleteBody(string path, string? confirmName, bool deleteFiles)
    {
        var body = new Generated.Models.DeleteRequest { Path = path, DeleteFiles = deleteFiles };
        if (confirmName is not null)
            body.ConfirmName = new Generated.Models.DeleteRequest.DeleteRequest_confirm_name { String = confirmName };
        return body;
    }

    /// <summary>
    /// confirm_name is a composed-type wrapper here too. Internal (not private)
    /// for the same reason as BuildDeleteBody: a client regeneration must not be
    /// able to change it silently.
    /// </summary>
    internal static Generated.Models.DeleteFolderRequest BuildDeleteFolderBody(string path, string? confirmName)
    {
        var body = new Generated.Models.DeleteFolderRequest { Path = path };
        if (confirmName is not null)
            body.ConfirmName = new Generated.Models.DeleteFolderRequest.DeleteFolderRequest_confirm_name { String = confirmName };
        return body;
    }
}
