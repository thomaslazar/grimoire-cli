using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class AddonsCommand
{
    public static Command Create()
    {
        var command = new Command("addons", "Install and manage metadata add-ons");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateRefreshCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("list", "List installed and available add-ons")
        {
            serverOption, tokenOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "available comes from the cached index — empty until addons refresh runs,",
            "and stale afterwards until it runs again. index_generated is when the",
            "cache was built.",
            "",
            "runnable false while enabled is true means the add-on is installed but",
            "blocked; blocked_reason says why. Only runnable add-ons appear as",
            "metadata sources.");
        command.AddExamples("grimoire-cli addons list");
        command.AddResponseExample<AddonListResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new AddonsService(client);
            var result = await service.ListAsync();
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.AddonListResponse);
            return 0;
        });
        return command;
    }

    private static Command CreateRefreshCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("refresh", "Fetch the add-on index")
        {
            serverOption, tokenOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Fetches index_url over the network; count is what the index offered.",
            "",
            "Installing needs a cached index, so a fresh instance runs this first.");
        command.AddExamples("grimoire-cli addons refresh");
        command.AddResponseExample<RefreshResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new AddonsService(client);
            var result = await service.RefreshAsync();
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.RefreshResult);
            return 0;
        });
        return command;
    }
}
