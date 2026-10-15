using System.Text;
using GrimoireCli.Api;
using GrimoireCli.Commands;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// The eight routes behind `campaigns wiki templates`: a campaign's note templates
/// (routers/campaigns/wiki_templates.py). Every route is owner-only, checked by the
/// server, so no permission hint is passed.
/// </summary>
public class CampaignWikiTemplateService
{
    private const string CampaignNotFound = "No campaign with that ID. List them with: grimoire-cli campaigns list";
    private const string TemplateNotFound = "No such campaign or template. List templates with: grimoire-cli campaigns wiki templates list --id <campaign-id>";

    private readonly GrimoireApiClient _client;

    public CampaignWikiTemplateService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/campaigns/{id}/wiki/templates.</summary>
    public async Task<string> ListAsync(string campaignId)
        => await _client.SendAsync(ListRequest(campaignId), notFoundHint: CampaignNotFound);

    /// <summary>GET /api/campaigns/{id}/wiki/templates/{template}.</summary>
    public async Task<string> GetAsync(string campaignId, string templateId)
        => await _client.SendAsync(GetRequest(campaignId, templateId), notFoundHint: TemplateNotFound);

    /// <summary>POST /api/campaigns/{id}/wiki/templates. The validated raw body is sent as-is.</summary>
    public async Task<string> CreateAsync(string campaignId, string rawBody)
        => await _client.SendAsync(CreateRequest(campaignId, rawBody), notFoundHint: CampaignNotFound);

    /// <summary>PATCH /api/campaigns/{id}/wiki/templates/{template}. Raw body, as <see cref="CreateAsync"/>.</summary>
    public async Task<string> UpdateAsync(string campaignId, string templateId, string rawBody)
        => await _client.SendAsync(UpdateRequest(campaignId, templateId, rawBody), notFoundHint: TemplateNotFound);

    /// <summary>DELETE /api/campaigns/{id}/wiki/templates/{template}.</summary>
    public async Task<string> DeleteAsync(string campaignId, string templateId)
        => await _client.SendAsync(DeleteRequest(campaignId, templateId), notFoundHint: TemplateNotFound);

    /// <summary>
    /// POST /api/campaigns/{id}/wiki/templates/upload. The part's filename is the
    /// local basename: the server picks its parser by extension.
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

    /// <summary>GET /api/campaigns/{id}/wiki/templates/{template}/export. A zip, as bytes.</summary>
    public async Task<Stream> ExportAsync(string campaignId, string templateId)
        => await _client.SendStreamAsync(ExportRequest(campaignId, templateId), notFoundHint: TemplateNotFound);

    /// <summary>POST /api/campaigns/{id}/wiki/templates/{template}/use. Always creates a new page.</summary>
    public async Task<string> UseAsync(string campaignId, string templateId)
        => await _client.SendAsync(UseRequest(campaignId, templateId), notFoundHint: TemplateNotFound);

    internal RequestInformation ListRequest(string campaignId)
        => _client.Api.Api.Campaigns[campaignId].Wiki.Templates.ToGetRequestInformation();

    internal RequestInformation GetRequest(string campaignId, string templateId)
        => _client.Api.Api.Campaigns[campaignId].Wiki.Templates[templateId].ToGetRequestInformation();

    internal RequestInformation CreateRequest(string campaignId, string rawBody)
    {
        var info = _client.Api.Api.Campaigns[campaignId].Wiki.Templates.ToPostRequestInformation(new Generated.Models.WikiTemplateInput());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return info;
    }

    internal RequestInformation UpdateRequest(string campaignId, string templateId, string rawBody)
    {
        var info = _client.Api.Api.Campaigns[campaignId].Wiki.Templates[templateId].ToPatchRequestInformation(new Generated.Models.WikiTemplateUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return info;
    }

    internal RequestInformation DeleteRequest(string campaignId, string templateId)
        => _client.Api.Api.Campaigns[campaignId].Wiki.Templates[templateId].ToDeleteRequestInformation();

    internal RequestInformation UseRequest(string campaignId, string templateId)
        => _client.Api.Api.Campaigns[campaignId].Wiki.Templates[templateId].Use.ToPostRequestInformation();

    internal RequestInformation ExportRequest(string campaignId, string templateId)
        => _client.Api.Api.Campaigns[campaignId].Wiki.Templates[templateId].Export.ToGetRequestInformation();

    internal RequestInformation ImportRequest(string campaignId, byte[] bytes, string fileName)
    {
        var body = new MultipartBody();
        body.AddOrReplacePart("file", "application/octet-stream", bytes, fileName);
        return _client.Api.Api.Campaigns[campaignId].Wiki.Templates.Upload.ToPostRequestInformation(body);
    }
}
