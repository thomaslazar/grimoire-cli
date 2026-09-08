using System.Text;
using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The thirteen duplicate-resolution endpoints, every one require_admin
/// (routers/duplicates/__init__.py registers no router-level dependency; each
/// handler takes require_admin itself). resource_type and accuracy reach the
/// generated models as enums because both are Literal upstream, so each is
/// parsed from the flag's string here. Enum.Parse cannot throw on that string:
/// every caller declares the flag as OptionHelpers.Choice over the same set, so
/// an unaccepted value is a parse error before any service call is made.
/// </summary>
public class DuplicatesService
{
    private const string AdminHint = "the admin role";
    private const string NotFoundHint =
        "No such item. List the candidate groups with: grimoire-cli duplicates groups";

    private readonly GrimoireApiClient _client;

    public DuplicatesService(GrimoireApiClient client) => _client = client;

    /// <summary>
    /// POST /api/duplicates/link. The body is validated and sent unchanged, so
    /// Kiota's own deserializer never sees it and the resource_type enum needs no
    /// mapping here.
    /// </summary>
    public async Task<string> LinkAsync(string rawBody)
    {
        var info = _client.Api.Api.Duplicates.Link.ToPostRequestInformation(
            new Generated.Models.LinkRequest());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: AdminHint);
    }

    /// <summary>POST /api/duplicates/promote. kind and label describe the old parent.</summary>
    public async Task<string> PromoteAsync(
        string resourceType, string newParentId, string oldParentId, string? kind, string? label)
    {
        var body = new Generated.Models.PromoteRequest
        {
            ResourceType = Enum.Parse<Generated.Models.PromoteRequest_resource_type>(resourceType, true),
            NewParentId = newParentId,
            OldParentId = oldParentId,
            Kind = kind,
            Label = label,
        };
        var info = _client.Api.Api.Duplicates.Promote.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/duplicates/unlink. parentId wins over ids server-side.</summary>
    public async Task<string> UnlinkAsync(string resourceType, string[] ids, string? parentId)
    {
        var info = _client.Api.Api.Duplicates.Unlink.ToPostRequestInformation(
            BuildUnlinkBody(resourceType, ids, parentId));
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/duplicates/merge-metadata.</summary>
    public async Task<string> MergeMetadataAsync(
        string resourceType, string sourceId, string targetId, string[] fields, bool overwrite)
    {
        var body = new Generated.Models.MergeMetadataRequest
        {
            ResourceType = Enum.Parse<Generated.Models.MergeMetadataRequest_resource_type>(resourceType, true),
            SourceId = sourceId,
            TargetId = targetId,
            Fields = [.. fields],
            Overwrite = overwrite,
        };
        var info = _client.Api.Api.Duplicates.MergeMetadata.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// DELETE /api/duplicates/items/{resource_type}/{item_id}. deleteFile decides
    /// only whether the file leaves the disk; the row and its references go either
    /// way.
    /// </summary>
    public async Task<string> DeleteItemAsync(
        string resourceType, string itemId, bool deleteFile, string? reparentTo)
    {
        var info = _client.Api.Api.Duplicates.Items[resourceType][itemId]
            .ToDeleteRequestInformation(BuildDeleteItemBody(deleteFile, reparentTo));
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>GET /api/duplicates/compare. Two to four ids, enforced server-side.</summary>
    public async Task<string> CompareAsync(string resourceType, string[] ids)
    {
        var info = _client.Api.Api.Duplicates.Compare.ToGetRequestInformation(c =>
        {
            c.QueryParameters.ResourceType = resourceType;
            c.QueryParameters.Ids = ids;
        });
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/duplicates/scan. 409 while a library scan is running.</summary>
    public async Task<string> ScanAsync(string[] resourceTypes, string? accuracy)
    {
        var body = new Generated.Models.ScanRequest();
        if (resourceTypes.Length > 0)
            body.ResourceTypes = [.. resourceTypes.Select(t =>
                (Generated.Models.ScanRequest_resource_types?)Enum.Parse<Generated.Models.ScanRequest_resource_types>(t, true))];
        if (accuracy is not null)
            body.Accuracy = Enum.Parse<Generated.Models.ScanRequest_accuracy>(accuracy, true);
        var info = _client.Api.Api.Duplicates.Scan.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint);
    }

    /// <summary>GET /api/duplicates/scan-status.</summary>
    public async Task<string> ScanStatusAsync()
    {
        var info = _client.Api.Api.Duplicates.ScanStatus.ToGetRequestInformation();
        return await _client.SendAsync(info, permissionHint: AdminHint);
    }

    /// <summary>POST /api/duplicates/cancel-scan.</summary>
    public async Task<string> CancelScanAsync()
    {
        var info = _client.Api.Api.Duplicates.CancelScan.ToPostRequestInformation();
        return await _client.SendAsync(info, permissionHint: AdminHint);
    }

    /// <summary>GET /api/duplicates/groups.</summary>
    public async Task<string> GroupsAsync(
        string? resourceType, double? minConfidence, int? limit, int? offset)
    {
        var info = _client.Api.Api.Duplicates.Groups.ToGetRequestInformation(c =>
        {
            c.QueryParameters.ResourceType = resourceType;
            c.QueryParameters.MinConfidence = minConfidence;
            c.QueryParameters.Limit = limit;
            c.QueryParameters.Offset = offset;
        });
        return await _client.SendAsync(info, permissionHint: AdminHint);
    }

    /// <summary>POST /api/duplicates/dismiss. At least two member ids, enforced server-side.</summary>
    public async Task<string> DismissAsync(string resourceType, string[] memberIds, string? note)
    {
        var body = new Generated.Models.DismissRequest
        {
            ResourceType = Enum.Parse<Generated.Models.DismissRequest_resource_type>(resourceType, true),
            MemberIds = [.. memberIds],
            Note = note,
        };
        var info = _client.Api.Api.Duplicates.Dismiss.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>GET /api/duplicates/dismissals.</summary>
    public async Task<string> DismissalsAsync(string? resourceType)
    {
        var info = _client.Api.Api.Duplicates.Dismissals.ToGetRequestInformation(c =>
            c.QueryParameters.ResourceType = resourceType);
        return await _client.SendAsync(info, permissionHint: AdminHint);
    }

    /// <summary>DELETE /api/duplicates/dismissals/{dismissal_id}.</summary>
    public async Task<string> UndismissAsync(string dismissalId)
    {
        var info = _client.Api.Api.Duplicates.Dismissals[dismissalId].ToDeleteRequestInformation();
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// parent_id is a composed-type wrapper because it is Optional upstream.
    /// Internal (not private) so a test can pin that a client regeneration cannot
    /// silently change it.
    /// </summary>
    internal static Generated.Models.UnlinkRequest BuildUnlinkBody(
        string resourceType, string[] ids, string? parentId)
    {
        var body = new Generated.Models.UnlinkRequest
        {
            ResourceType = Enum.Parse<Generated.Models.UnlinkRequest_resource_type>(resourceType, true),
            Ids = [.. ids],
        };
        if (parentId is not null)
            body.ParentId = new Generated.Models.UnlinkRequest.UnlinkRequest_parent_id { String = parentId };
        return body;
    }

    /// <summary>
    /// reparent_to is a composed-type wrapper; assigning it only when the flag was
    /// given is what keeps "" (promote every variant to standalone) distinct from
    /// omitted (refuse if the item has any).
    /// </summary>
    internal static Generated.Models.DeleteItemRequest BuildDeleteItemBody(
        bool deleteFile, string? reparentTo)
    {
        var body = new Generated.Models.DeleteItemRequest { DeleteFile = deleteFile };
        if (reparentTo is not null)
            body.ReparentTo = new Generated.Models.DeleteItemRequest.DeleteItemRequest_reparent_to { String = reparentTo };
        return body;
    }
}
