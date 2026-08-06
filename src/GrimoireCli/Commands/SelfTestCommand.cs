using System.CommandLine;
using System.Text.Json;
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Models;

namespace GrimoireCli.Commands;

/// <summary>
/// Offline integrity check for the published binary. Native AOT trims
/// reflection-based serialization, so a missing [JsonSerializable] registration
/// compiles fine and then fails at runtime on a user's machine. CI runs this
/// against every published RID to catch that before release.
/// </summary>
public static class SelfTestCommand
{
    public static Command Create()
    {
        var command = new Command("self-test", "Verify the binary works (offline; no server needed)");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Exercises source-generated JSON, JWT parsing and version comparison.",
            "Exits 0 on success, 1 on the first failed check.");
        command.SetAction(parseResult =>
        {
            var failures = new List<string>();

            // Source-generated JSON round-trip: the AOT trap.
            var config = new AppConfig { Server = "https://example.invalid", AccessToken = "abc" };
            var json = JsonSerializer.Serialize(config, AppJsonContext.Default.AppConfig);
            var back = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppConfig);
            if (back?.Server != config.Server || back.AccessToken != config.AccessToken)
                failures.Add("AppConfig JSON round-trip failed");

            var request = JsonSerializer.Serialize(
                new LoginRequest { Username = "u", Password = "p" }, AppJsonContext.Default.LoginRequest);
            if (!request.Contains("\"username\"") || !request.Contains("\"password\""))
                failures.Add("LoginRequest did not serialize with snake_case-free API field names");

            var dict = JsonSerializer.Serialize(
                new Dictionary<string, string> { ["k"] = "v" }, AppJsonContext.Default.DictionaryStringString);
            if (!dict.Contains("\"k\""))
                failures.Add("Dictionary<string,string> JSON round-trip failed");

            // Token parsing: base64url payload {"exp":4102444800} = 2100-01-01.
            const string token = "eyJhbGciOiJIUzI1NiJ9.eyJleHAiOjQxMDI0NDQ4MDB9.sig";
            if (TokenHelper.GetExpiration(token)?.Year != 2100)
                failures.Add("TokenHelper failed to read the exp claim");
            if (TokenHelper.IsExpiringSoon(token))
                failures.Add("TokenHelper reported a 2100 expiry as expiring soon");

            // Version comparison, including the tolerant parse path.
            if (GrimoireApiClient.CompareVersions("1.5.4", "1.5.4") != 0
                || GrimoireApiClient.CompareVersions("1.6.0", "1.5.4") <= 0
                || GrimoireApiClient.CompareVersions("v1.5.3", "1.5.4") >= 0
                || GrimoireApiClient.CompareVersions("1.5.4-rc1", "1.5.4") != 0)
                failures.Add("CompareVersions produced a wrong ordering");

            // Login-response token extraction across the plausible spellings.
            if (GrimoireApiClient.ExtractToken("{\"access_token\":\"t\"}") != "t"
                || GrimoireApiClient.ExtractToken("{\"token\":\"t\"}") != "t"
                || GrimoireApiClient.ExtractToken("{\"nope\":1}") != null
                || GrimoireApiClient.ExtractToken("not json") != null)
                failures.Add("ExtractToken did not handle the expected response bodies");

            foreach (var failure in failures)
                Console.Error.WriteLine($"FAIL: {failure}");
            if (failures.Count > 0)
            {
                Environment.Exit(1);
                return 1;
            }
            Console.Error.WriteLine("self-test passed");
            return 0;
        });
        return command;
    }
}
