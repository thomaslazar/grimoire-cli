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
        command.Subcommands.Add(CreateUpgradeAllCommand());
        command.Subcommands.Add(CreateUninstallCommand());
        command.Subcommands.Add(CreateSettingsCommand());
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

    private static Command CreateUpgradeAllCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("upgrade-all", "Upgrade every installed add-on")
        {
            serverOption, tokenOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Refreshes the index first, and carries on with the cached one if that",
            "fails.",
            "",
            "Skip-and-continue: an add-on that cannot be upgraded lands in failed and",
            "the rest still upgrade. Exit 3 is HTTP 200 with a non-empty failed list.",
            "",
            "Script approval is not carried over, so a script-backed add-on is",
            "unapproved until re-approved with install --approve-script.");
        command.AddExamples("grimoire-cli addons upgrade-all");
        command.AddResponseExample<UpgradeAllResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new AddonsService(client);
            var result = await service.UpgradeAllAsync();
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.UpgradeAllResult);
            return BulkExit.CodeFor(result.Failed);
        });
        return command;
    }

    private static Command CreateSettingsCommand()
    {
        var indexUrlOption = new Option<string?>("--index-url") { Description = "Add-on index URL" };
        var allowScriptsOption = new Option<bool?>("--allow-scripts") { Description = "Allow add-on scripts to run (true | false)" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("settings", "Set the add-on index URL and script switch")
        {
            indexUrlOption, allowScriptsOption, serverOption, tokenOption
        };
        command.AddRoleRequired("admin");
        command.Validators.Add(result =>
        {
            if (result.GetValue(indexUrlOption) is null && result.GetValue(allowScriptsOption) is null)
                result.AddError("Pass --index-url, --allow-scripts, or both.");
        });
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "At least one flag is required.",
            "",
            "Changing --index-url does not refetch; run addons refresh after.",
            "",
            "--allow-scripts is the global switch. An add-on that ships a script also",
            "needs its own approval, from install --approve-script.");
        command.AddExamples("grimoire-cli addons settings --allow-scripts true");
        command.AddResponseExample<AddonSettings>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new AddonsService(client);
            var result = await service.SettingsAsync(
                parseResult.GetValue(indexUrlOption), parseResult.GetValue(allowScriptsOption));
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.AddonSettings);
            return 0;
        });
        return command;
    }
}
