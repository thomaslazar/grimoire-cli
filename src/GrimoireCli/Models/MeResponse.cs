using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

public class MeResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    // One of admin, gm, player, guest (backend/auth.py:45). Writes need gm or admin.
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("allow_explicit")]
    public bool AllowExplicit { get; set; }

    // A bool, not a list: the server collapses the user's campaign_access column
    // to "has access to any campaign" before sending it (routers/auth/core.py:184).
    [JsonPropertyName("campaign_access")]
    public bool CampaignAccess { get; set; }

    [JsonPropertyName("oidc_linked")]
    public bool OidcLinked { get; set; }
}
