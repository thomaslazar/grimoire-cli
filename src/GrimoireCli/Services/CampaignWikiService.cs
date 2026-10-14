using System.Text;
using GrimoireCli.Api;
using GrimoireCli.Commands;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// The twelve routes behind `campaigns wiki`: a campaign's wiki pages. Each request
/// is built by an internal method so a test can pin its route and method; the
/// public methods send it. Access is checked inside the server's handlers, not by
/// role, so no permission hint is passed — the server's own 403 detail explains.
/// </summary>
public class CampaignWikiService
{
    private const string CampaignNotFound = "No campaign with that ID. List them with: grimoire-cli campaigns list";
    private const string PageNotFound = "No such campaign or page. List pages with: grimoire-cli campaigns wiki list --id <campaign-id>";

    private readonly GrimoireApiClient _client;

    public CampaignWikiService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/campaigns/{id}/wiki. Visible pages, hidden ones excluded unless asked for.</summary>
    public async Task<string> ListAsync(string campaignId, bool mine, bool includeHidden)
        => await _client.SendAsync(ListRequest(campaignId, mine, includeHidden), notFoundHint: CampaignNotFound);

    /// <summary>GET /api/campaigns/{id}/wiki/{page}. Non-owners get the body with GM secrets removed.</summary>
    public async Task<string> GetAsync(string campaignId, string pageId)
        => await _client.SendAsync(GetRequest(campaignId, pageId), notFoundHint: PageNotFound);

    /// <summary>POST /api/campaigns/{id}/wiki. The validated raw body is sent as-is.</summary>
    public async Task<string> CreateAsync(string campaignId, string rawBody)
        => await _client.SendAsync(CreateRequest(campaignId, rawBody), notFoundHint: CampaignNotFound);

    /// <summary>PATCH /api/campaigns/{id}/wiki/{page}. Raw body, as <see cref="CreateAsync"/>.</summary>
    public async Task<string> UpdateAsync(string campaignId, string pageId, string rawBody)
        => await _client.SendAsync(UpdateRequest(campaignId, pageId, rawBody), notFoundHint: PageNotFound);

    /// <summary>DELETE /api/campaigns/{id}/wiki/{page}. An unknown page is a silent 204.</summary>
    public async Task<string> DeleteAsync(string campaignId, string pageId)
        => await _client.SendAsync(DeleteRequest(campaignId, pageId), notFoundHint: CampaignNotFound);

    /// <summary>GET /api/campaigns/{id}/wiki/search. Plain substring over title and body.</summary>
    public async Task<string> SearchAsync(string campaignId, string query)
        => await _client.SendAsync(SearchRequest(campaignId, query), notFoundHint: CampaignNotFound);

    /// <summary>GET /api/campaigns/{id}/wiki/titles. Ids, titles and headings for links.</summary>
    public async Task<string> TitlesAsync(string campaignId)
        => await _client.SendAsync(TitlesRequest(campaignId), notFoundHint: CampaignNotFound);

    /// <summary>PUT /api/campaigns/{id}/wiki/reorder. Pages the caller cannot edit keep their slots.</summary>
    public async Task<string> ReorderAsync(string campaignId, string[] orderedIds)
        => await _client.SendAsync(ReorderRequest(campaignId, orderedIds), notFoundHint: CampaignNotFound);

    /// <summary>POST /api/campaigns/{id}/wiki/{page}/hide. Only the caller's own view.</summary>
    public async Task<string> HideAsync(string campaignId, string pageId)
        => await _client.SendAsync(HideRequest(campaignId, pageId), notFoundHint: PageNotFound);

    /// <summary>GET /api/campaigns/{id}/wiki/export. A zip, a markdown file or a JSON bundle, as bytes.</summary>
    public async Task<Stream> ExportAsync(string campaignId, string? format)
        => await _client.SendStreamAsync(ExportRequest(campaignId, format), notFoundHint: CampaignNotFound);

    /// <summary>
    /// POST /api/campaigns/{id}/wiki/import. The part's filename is the local
    /// basename: the server picks its parser by extension.
    /// </summary>
    public async Task<string> ImportAsync(string campaignId, string filePath)
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
        return await _client.SendAsync(ImportRequest(campaignId, bytes, Path.GetFileName(filePath)), notFoundHint: CampaignNotFound);
    }

    /// <summary>DELETE /api/campaigns/{id}/wiki/{page}/hide.</summary>
    public async Task<string> UnhideAsync(string campaignId, string pageId)
        => await _client.SendAsync(UnhideRequest(campaignId, pageId), notFoundHint: CampaignNotFound);

    internal RequestInformation ListRequest(string campaignId, bool mine, bool includeHidden)
        => _client.Api.Api.Campaigns[campaignId].Wiki.ToGetRequestInformation(c =>
        {
            // Sent only when set: the server defaults both to false.
            if (mine) c.QueryParameters.Mine = true;
            if (includeHidden) c.QueryParameters.IncludeHidden = true;
        });

    internal RequestInformation GetRequest(string campaignId, string pageId)
        => _client.Api.Api.Campaigns[campaignId].Wiki[pageId].ToGetRequestInformation();

    internal RequestInformation CreateRequest(string campaignId, string rawBody)
    {
        var info = _client.Api.Api.Campaigns[campaignId].Wiki.ToPostRequestInformation(new Generated.Models.WikiPageCreate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return info;
    }

    internal RequestInformation UpdateRequest(string campaignId, string pageId, string rawBody)
    {
        var info = _client.Api.Api.Campaigns[campaignId].Wiki[pageId].ToPatchRequestInformation(new Generated.Models.WikiPageUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return info;
    }

    internal RequestInformation DeleteRequest(string campaignId, string pageId)
        => _client.Api.Api.Campaigns[campaignId].Wiki[pageId].ToDeleteRequestInformation();

    internal RequestInformation SearchRequest(string campaignId, string query)
        => _client.Api.Api.Campaigns[campaignId].Wiki.Search.ToGetRequestInformation(c => c.QueryParameters.Q = query);

    internal RequestInformation TitlesRequest(string campaignId)
        => _client.Api.Api.Campaigns[campaignId].Wiki.Titles.ToGetRequestInformation();

    internal RequestInformation ReorderRequest(string campaignId, string[] orderedIds)
        => _client.Api.Api.Campaigns[campaignId].Wiki.Reorder.ToPutRequestInformation(
            new Generated.Models.WikiReorder { OrderedIds = [.. orderedIds] });

    internal RequestInformation HideRequest(string campaignId, string pageId)
        => _client.Api.Api.Campaigns[campaignId].Wiki[pageId].Hide.ToPostRequestInformation();

    internal RequestInformation UnhideRequest(string campaignId, string pageId)
        => _client.Api.Api.Campaigns[campaignId].Wiki[pageId].Hide.ToDeleteRequestInformation();

    internal RequestInformation ExportRequest(string campaignId, string? format)
        => _client.Api.Api.Campaigns[campaignId].Wiki.Export.ToGetRequestInformation(c =>
        {
            // Sent only when set: the server defaults to md.
            if (format is not null) c.QueryParameters.Format = format;
        });

    internal RequestInformation ImportRequest(string campaignId, byte[] bytes, string fileName)
    {
        var body = new MultipartBody();
        body.AddOrReplacePart("file", "application/octet-stream", bytes, fileName);
        return _client.Api.Api.Campaigns[campaignId].Wiki.Import.ToPostRequestInformation(body);
    }
}
