using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class FolderAccessCommands
{
    private static readonly string[] Levels = ["gm", "admin"];

    public static Command Create()
    {
        var command = new Command("access", "Restrict every book beneath a folder by role");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateSetCommand());
        command.Subcommands.Add(CreateRemoveCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "Folder access rules");
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "exists false: the folder left disk outside the file manager, so the",
            "rule covers nothing.");
        command.AddExamples("grimoire-cli files folder access list");
        command.AddResponseExample<Generated.Models.FolderAccessRulesResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new FolderAccessService(client).ListAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateSetCommand()
    {
        var pathOption = new Option<string>("--path") { Description = "Folder from the library root", Required = true };
        var levelOption = OptionHelpers.Choice("--level", "Who may still see the books", Levels);
        levelOption.Required = true;
        var command = new Command("set", "Restrict a folder, replacing any level it had")
        {
            pathOption, levelOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--path must be a folder inside books/ or armory/, not the root itself",
            "(400); a missing folder is 404.",
            "",
            "A book gets the stricter of the rule and its system's or category's",
            "level; its own access_level overrides both.",
            "",
            "Campaign shares of the covered books are demoted to GM-only and league",
            "shares to organiser-only; remove does not restore them.",
            "",
            "The rule follows the folder on files move.");
        command.AddExamples("grimoire-cli files folder access set --path \"books/Shadowrun/Adventures\" --level gm");
        command.AddResponseExample<Generated.Models.FolderAccessRuleOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new FolderAccessService(client).SetAsync(
                parseResult.GetValue(pathOption)!, parseResult.GetValue(levelOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateRemoveCommand()
    {
        var pathOption = new Option<string>("--path") { Description = "Restricted folder", Required = true };
        var command = new Command("remove", "Lift a folder's restriction")
        {
            pathOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "404 when the folder has no rule. Campaign and league shares the rule",
            "demoted stay demoted.");
        command.AddExamples("grimoire-cli files folder access remove --path \"books/Shadowrun/Adventures\"");
        command.AddResponseExample<Generated.Models.FolderAccessStatusResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new FolderAccessService(client).RemoveAsync(parseResult.GetValue(pathOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
