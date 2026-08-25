using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class MeCommand
{
    public static Command Create()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("me", "Show the authenticated account")
        {
            serverOption, tokenOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "role is admin, gm, player or guest. Writes need gm or admin.",
            "",
            "Called with a bearer token and no cookie, the server sets a session",
            "cookie on the response. The CLI stores no cookies.");
        command.AddExamples(
            "grimoire-cli me",
            "grimoire-cli me | jq -r .role");
        command.AddResponseExample<Generated.Models.AuthMeResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var result = await new AuthService(client).MeAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
