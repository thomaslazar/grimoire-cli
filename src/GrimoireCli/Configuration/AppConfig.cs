using System.Text.Json.Serialization;

namespace GrimoireCli.Configuration;

public class AppConfig
{
    [JsonPropertyName("server")]
    public string? Server { get; set; }

    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    // Written by the CLI's version-check cadence, not by the operator: when the
    // server version was last checked, and what it was.
    [JsonPropertyName("lastVersionCheck")]
    public DateTimeOffset? LastVersionCheck { get; set; }

    [JsonPropertyName("lastServerVersion")]
    public string? LastServerVersion { get; set; }
}
