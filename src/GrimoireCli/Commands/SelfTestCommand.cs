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

            // The generated LoginRequest is the wire body login actually sends (see
            // GrimoireApiClient.LoginAsync) — build it the same way, offline, so this
            // covers the path that runs rather than a hand-written stand-in for it.
            var loginAdapter = new Microsoft.Kiota.Http.HttpClientLibrary.HttpClientRequestAdapter(
                new Microsoft.Kiota.Abstractions.Authentication.AnonymousAuthenticationProvider());
            var loginInfo = new GrimoireCli.Generated.GrimoireApiClient(loginAdapter).Api.Auth.Login
                .ToPostRequestInformation(new GrimoireCli.Generated.Models.LoginRequest { Username = "u", Password = "p" });
            using var loginBody = new StreamReader(loginInfo.Content!);
            var request = loginBody.ReadToEnd();
            if (!request.Contains("\"username\"") || !request.Contains("\"password\""))
                failures.Add("Generated LoginRequest did not serialize with the expected field names");

            var dict = JsonSerializer.Serialize(
                new Dictionary<string, string> { ["k"] = "v" }, AppJsonContext.Default.DictionaryStringString);
            if (!dict.Contains("\"k\""))
                failures.Add("Dictionary<string,string> JSON round-trip failed");

            // Response DTOs: these cross the JSON boundary on every command that talks
            // to the API, not just login, so they need the same AOT coverage.
            var link = new LinkEntry { Label = "wiki", Url = "https://example.invalid/wiki" };
            var linkBack = JsonSerializer.Deserialize(
                JsonSerializer.Serialize(link, AppJsonContext.Default.LinkEntry), AppJsonContext.Default.LinkEntry);
            if (linkBack?.Label != link.Label || linkBack.Url != link.Url)
                failures.Add("LinkEntry JSON round-trip failed");

            var publisher = new PublisherEntry { Name = "Pegasus", Url = "https://example.invalid/pegasus" };
            var publisherBack = JsonSerializer.Deserialize(
                JsonSerializer.Serialize(publisher, AppJsonContext.Default.PublisherEntry), AppJsonContext.Default.PublisherEntry);
            if (publisherBack?.Name != publisher.Name || publisherBack.Url != publisher.Url)
                failures.Add("PublisherEntry JSON round-trip failed");

            var book = new Book { Id = "b1", Title = "Shadowrun 6 DE", PageCount = 42 };
            var bookBack = JsonSerializer.Deserialize(
                JsonSerializer.Serialize(book, AppJsonContext.Default.Book), AppJsonContext.Default.Book);
            if (bookBack?.Id != book.Id || bookBack.Title != book.Title || bookBack.PageCount != book.PageCount)
                failures.Add("Book JSON round-trip failed");

            var summary = new GameSystemSummary { Id = "s1", Name = "Shadowrun 6 DE", BookCount = 227 };
            var summaryBack = JsonSerializer.Deserialize(
                JsonSerializer.Serialize(summary, AppJsonContext.Default.GameSystemSummary), AppJsonContext.Default.GameSystemSummary);
            if (summaryBack?.Id != summary.Id || summaryBack.Name != summary.Name || summaryBack.BookCount != summary.BookCount)
                failures.Add("GameSystemSummary JSON round-trip failed");

            var detail = new GameSystemDetail { Id = "s1", Name = "Shadowrun 6 DE", Books = [book] };
            var detailBack = JsonSerializer.Deserialize(
                JsonSerializer.Serialize(detail, AppJsonContext.Default.GameSystemDetail), AppJsonContext.Default.GameSystemDetail);
            if (detailBack?.Id != detail.Id || detailBack.Books?.Count != 1 || detailBack.Books[0].Title != book.Title)
                failures.Add("GameSystemDetail JSON round-trip failed");

            var summaryList = new List<GameSystemSummary> { summary };
            var summaryListBack = JsonSerializer.Deserialize(
                JsonSerializer.Serialize(summaryList, AppJsonContext.Default.ListGameSystemSummary), AppJsonContext.Default.ListGameSystemSummary);
            if (summaryListBack?.Count != 1 || summaryListBack[0].Id != summary.Id)
                failures.Add("List<GameSystemSummary> JSON round-trip failed");

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

            // The User-Agent reads an assembly-level attribute, which trimming can
            // strip: it compiles fine and goes empty only in a published binary.
            var version = GrimoireApiClient.ClientVersion;
            var assemblyVersion = typeof(GrimoireApiClient).Assembly.GetName().Version?.ToString(3);
            if (string.IsNullOrWhiteSpace(version) || version == "0.0.0")
                failures.Add("Informational version did not resolve");
            else if (assemblyVersion != null && !version.StartsWith(assemblyVersion, StringComparison.Ordinal))
                failures.Add($"Informational version '{version}' does not start with assembly version '{assemblyVersion}'");

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
