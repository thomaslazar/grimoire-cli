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
        command.Subcommands.Add(CreateInstallCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateUninstallCommand());
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

    private static Command CreateInstallCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Add-on ID", Required = true };
        var approveOption = new Option<bool>("--approve-script")
        {
            Description = "Consent to run this add-on's script; ignored when it ships none",
        };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("install", "Install or upgrade one add-on")
        {
            idOption, approveOption, serverOption, tokenOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Takes an id from available in addons list; 400 if the index has no such",
            "entry, 502 if the download fails. The manifest is",
            "verified against the index's digest.",
            "",
            "Also upgrades: re-running on an installed add-on replaces it.",
            "",
            "--approve-script is consent to run third-party code, recorded against",
            "the script's digest and ignored for add-ons that ship no script. An",
            "upgrade that changes the script drops back to unapproved.");
        command.AddExamples("grimoire-cli addons install --id <addon-id> --approve-script");
        command.AddResponseExample<AddonInstalled>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new AddonsService(client);
            var result = await service.InstallAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(approveOption));
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.AddonInstalled);
            return 0;
        });
        return command;
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Add-on ID", Required = true };
        var enabledOption = new Option<bool?>("--enabled") { Description = "Enable or disable the add-on (true | false)" };
        var scriptApprovedOption = new Option<bool?>("--script-approved") { Description = "Grant or revoke script approval (true | false)" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("update", "Enable, disable, or approve one add-on")
        {
            idOption, enabledOption, scriptApprovedOption, serverOption, tokenOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Changes state, never version — upgrade with install or upgrade-all.",
            "",
            "404 if no such add-on is installed.");
        command.AddExamples("grimoire-cli addons update --id <addon-id> --enabled false");
        command.AddResponseExample<AddonInstalled>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new AddonsService(client);
            var result = await service.UpdateAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(enabledOption),
                parseResult.GetValue(scriptApprovedOption));
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.AddonInstalled);
            return 0;
        });
        return command;
    }

    private static Command CreateUninstallCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Add-on ID", Required = true };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("uninstall", "Remove one add-on")
        {
            idOption, serverOption, tokenOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Removes the add-on's directory and forgets its state; reinstall with",
            "addons install --id. 404 if it is not installed.",
            "",
            "Responds {\"status\": \"ok\"}.");
        command.AddExamples("grimoire-cli addons uninstall --id <addon-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new AddonsService(client);
            var response = await service.UninstallAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(response);
            return 0;
        });
        return command;
    }
}
