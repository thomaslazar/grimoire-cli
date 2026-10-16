using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignMembersCommands
{
    public static Command Create()
    {
        var command = new Command("members", "Who plays in a campaign");
        command.Subcommands.Add(CreateEligibleCommand());
        command.Subcommands.Add(CreateInviteCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateRemoveCommand());
        return command;
    }

    private static Option<string> CampaignIdOption() =>
        new("--id") { Description = "Campaign ID", Required = true };

    private static Option<string> UserIdOption() =>
        new("--user-id") { Description = "User ID (from members eligible or the campaign's members)", Required = true };

    private static Command CreateEligibleCommand()
    {
        var idOption = CampaignIdOption();
        var command = new Command("eligible", "Users who can be invited") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, ["Campaign owner only. Registered users other than you; guests are never listed."]);
        command.AddExamples("grimoire-cli campaigns members eligible --id <campaign-id>");
        command.AddResponseExampleArray<Generated.Models.EligibleMemberOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignMembersService(client).EligibleAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateInviteCommand()
    {
        var idOption = CampaignIdOption();
        var userIdOption = UserIdOption();
        var command = new Command("invite", "Invite a user to the campaign") { idOption, userIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            ["Campaign owner only; refused while the campaign is archived. Group",
            "campaigns only (400). 409 if already invited or a member; 403 if their",
            "campaign access is disabled."]);
        command.AddExamples("grimoire-cli campaigns members invite --id <campaign-id> --user-id <user-id>");
        command.AddResponseExample<Generated.Models.MemberInviteOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignMembersService(client).InviteAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(userIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = CampaignIdOption();
        var userIdOption = UserIdOption();
        var statusOption = OptionHelpers.Choice("--status", "Your answer to the invitation", ["accepted", "declined"]);
        var nameOption = new Option<string?>("--character-name") { Description = "Character name" };
        var sheetOption = new Option<string?>("--character-sheet-url") { Description = "Link to the character sheet" };
        var command = new Command("update", "Answer an invitation or set a member's character") { idOption, userIdOption, statusOption, nameOption, sheetOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            ["Yourself, or the campaign owner for anyone; refused while the campaign",
            "is archived. Send only what changes; \"\" clears a name or link. A sheet",
            "link replaces any uploaded sheet file. Accepting needs your campaign",
            "access (403)."]);
        command.AddExamples(
            "grimoire-cli campaigns members update --id <campaign-id> --user-id <user-id> --status accepted",
            "grimoire-cli campaigns members update --id <campaign-id> --user-id <user-id> --character-name \"Aria\"");
        command.AddResponseExample<Generated.Models.MemberStatusOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignMembersService(client).UpdateAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(userIdOption)!,
                parseResult.GetValue(statusOption), parseResult.GetValue(nameOption), parseResult.GetValue(sheetOption)));
            return 0;
        });
        return command;
    }

    private static Command CreateRemoveCommand()
    {
        var idOption = CampaignIdOption();
        var userIdOption = UserIdOption();
        var command = new Command("remove", "Remove a member, or leave") { idOption, userIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            ["Yourself, which works even while the campaign is archived, or the",
            "campaign owner for anyone. Removing a guest also deletes their account.",
            "An unknown user is a silent 204."]);
        command.AddExamples("grimoire-cli campaigns members remove --id <campaign-id> --user-id <user-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignMembersService(client).RemoveAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(userIdOption)!));
            return 0;
        });
        return command;
    }
}
