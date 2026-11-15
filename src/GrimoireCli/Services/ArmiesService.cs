using System.Text;
using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// The routes behind `armies`: wargame army lists. Each request is built by an
/// internal method so a test can pin its route and method; the public methods
/// send it. Access is decided inside the server's handlers (owner, admin, league
/// membership), not by role, so no permission hint is passed.
/// </summary>
public class ArmiesService
{
    private const string ArmyNotFound = "No army with that ID. List yours with: grimoire-cli armies list";

    private readonly GrimoireApiClient _client;

    public ArmiesService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/armies. The caller's own armies.</summary>
    public async Task<string> ListAsync() => await _client.SendAsync(ListRequest());

    /// <summary>GET /api/armies/{id}.</summary>
    public async Task<string> GetAsync(string id) => await _client.SendAsync(GetRequest(id), notFoundHint: ArmyNotFound);

    /// <summary>POST /api/armies. The validated raw body is sent as-is.</summary>
    public async Task<string> CreateAsync(string rawBody) => await _client.SendAsync(CreateRequest(rawBody));

    /// <summary>PATCH /api/armies/{id}. Raw body, as <see cref="CreateAsync"/>.</summary>
    public async Task<string> UpdateAsync(string id, string rawBody)
        => await _client.SendAsync(UpdateRequest(id, rawBody), notFoundHint: ArmyNotFound);

    /// <summary>DELETE /api/armies/{id}.</summary>
    public async Task<string> DeleteAsync(string id) => await _client.SendAsync(DeleteRequest(id), notFoundHint: ArmyNotFound);

    /// <summary>POST /api/armies/import. A BattleScribe or New Recruit roster, with an optional name.</summary>
    public async Task<string> ImportAsync(string filePath, string? name)
        => await _client.SendUploadAsync(ImportRequest(FileUpload.Open(filePath), filePath, name));

    /// <summary>GET /api/armies/{id}/export. The army as text or as the imported roster file.</summary>
    public async Task<Stream> ExportAsync(string id, string? format)
        => await _client.SendStreamAsync(ExportRequest(id, format), notFoundHint: ArmyNotFound);

    /// <summary>GET /api/armies/{id}/publications.</summary>
    public async Task<string> PublicationsAsync(string id)
        => await _client.SendAsync(PublicationsRequest(id), notFoundHint: ArmyNotFound);

    /// <summary>PUT /api/armies/{id}/units/{unitId}/progress. Replaces the unit's whole entry.</summary>
    public async Task<string> ProgressAsync(string id, string unitId, int experience, string[] honours, string? notes)
        => await _client.SendAsync(ProgressRequest(id, unitId, experience, honours, notes), notFoundHint: ArmyNotFound);

    /// <summary>GET /api/armies/games.</summary>
    public async Task<string> GamesAsync() => await _client.SendAsync(GamesRequest());

    /// <summary>GET /api/armies/games/{systemId}/factions.</summary>
    public async Task<string> FactionsAsync(string systemId)
        => await _client.SendAsync(FactionsRequest(systemId), notFoundHint: "No such wargame system. List them with: grimoire-cli armies games");

    internal RequestInformation ListRequest() => _client.Api.Api.Armies.ToGetRequestInformation();

    internal RequestInformation GetRequest(string id) => _client.Api.Api.Armies[id].ToGetRequestInformation();

    internal RequestInformation CreateRequest(string rawBody)
    {
        var info = _client.Api.Api.Armies.ToPostRequestInformation(new Generated.Models.ArmyCreate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return info;
    }

    internal RequestInformation UpdateRequest(string id, string rawBody)
    {
        var info = _client.Api.Api.Armies[id].ToPatchRequestInformation(new Generated.Models.ArmyUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return info;
    }

    internal RequestInformation DeleteRequest(string id) => _client.Api.Api.Armies[id].ToDeleteRequestInformation();

    internal RequestInformation ImportRequest(Stream file, string filePath, string? name)
    {
        var fields = new List<(string, string)>();
        if (name is not null)
            fields.Add(("name", name));
        return FileUpload.Request(
            b => _client.Api.Api.Armies.Import.ToPostRequestInformation(b),
            new StreamedMultipartBody(fields, Path.GetFileName(filePath), "application/octet-stream", file));
    }

    internal RequestInformation ExportRequest(string id, string? format)
        => _client.Api.Api.Armies[id].Export.ToGetRequestInformation(c =>
        {
            // Sent only when set: the server defaults to text.
            if (format is not null) c.QueryParameters.Format = format;
        });

    internal RequestInformation PublicationsRequest(string id) => _client.Api.Api.Armies[id].Publications.ToGetRequestInformation();

    internal RequestInformation ProgressRequest(string id, string unitId, int experience, string[] honours, string? notes)
        => _client.Api.Api.Armies[id].Units[unitId].Progress.ToPutRequestInformation(BuildProgressBody(experience, honours, notes));

    /// <summary>Every field is sent: the PUT replaces the unit's whole entry.</summary>
    internal static Generated.Models.UnitProgress BuildProgressBody(int experience, string[] honours, string? notes)
        => new() { Experience = experience, Honours = [.. honours], Notes = notes ?? "" };

    internal RequestInformation GamesRequest() => _client.Api.Api.Armies.Games.ToGetRequestInformation();

    internal RequestInformation FactionsRequest(string systemId) => _client.Api.Api.Armies.Games[systemId].Factions.ToGetRequestInformation();
}
