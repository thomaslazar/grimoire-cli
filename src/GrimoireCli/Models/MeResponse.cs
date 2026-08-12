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

    // Whether the user may create campaigns, be added to new campaigns, and manage
    // campaign content (backend/models/users.py:34-37). It is a plain boolean
    // permission column; the server treats a NULL value as enabled rather than
    // sending it through (routers/auth/core.py:176).
    [JsonPropertyName("campaign_access")]
    public bool CampaignAccess { get; set; }

    [JsonPropertyName("oidc_linked")]
    public bool OidcLinked { get; set; }
}
