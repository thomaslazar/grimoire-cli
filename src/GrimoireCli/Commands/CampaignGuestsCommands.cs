using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignGuestsCommands
{
    private const string Owned = "Campaign owner only; group campaigns only (400); 409 while archived.";
    private const string Code = "The code is the guest's login; anyone holding it is that guest.";

    public static Command Create()
    {
        var command = new Command("guests", "Guest invite codes for players without an account");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateRegenerateCommand());
        command.Subcommands.Add(CreateRemoveCommand());
        command.Subcommands.Add(CreateShareCommand());
        return command;
    }

    private static Option<string> CampaignIdOption() =>
        new("--id") { Description = "Campaign ID", Required = true };

    private static Option<string> MemberIdOption() =>
        new("--member-id") { Description = "Guest's member ID (id from guests list, not user_id)", Required = true };

    private static Command CreateListCommand()
    {
        var idOption = CampaignIdOption();
        var command = new Command("list", "List the campaign's guests and their codes") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, [Owned, Code]);
        command.AddExamples("grimoire-cli campaigns guests list --id <campaign-id>");
        command.AddResponseExampleArray<Generated.Models.Backend__routers__campaigns___response_schemas__GuestOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignGuestsService(client).ListAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateCreateCommand()
    {
        var idOption = CampaignIdOption();
        var nicknameOption = new Option<string?>("--nickname") { Description = "Name shown for the guest (default Guest)" };
        var command = new Command("create", "Create a guest and their invite code") { idOption, nicknameOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [Owned, "403 when the server has guest access off. The guest is a member at once.", Code]);
        command.AddExamples("grimoire-cli campaigns guests create --id <campaign-id> --nickname \"Sam\"");
        command.AddResponseExample<Generated.Models.Backend__routers__campaigns___response_schemas__GuestOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignGuestsService(client).CreateAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(nicknameOption)));
            return 0;
        });
        return command;
    }

    private static Command CreateRegenerateCommand()
    {
        var idOption = CampaignIdOption();
        var memberIdOption = MemberIdOption();
        var command = new Command("regenerate", "Replace a guest's invite code") { idOption, memberIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [Owned, "403 when the server has guest access off. The old code stops working",
            "and the guest's sessions end.", Code]);
        command.AddExamples("grimoire-cli campaigns guests regenerate --id <campaign-id> --member-id <member-id>");
        command.AddResponseExample<Generated.Models.Backend__routers__campaigns___response_schemas__GuestOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignGuestsService(client).RegenerateAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(memberIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateRemoveCommand()
    {
        var idOption = CampaignIdOption();
        var memberIdOption = MemberIdOption();
        var command = new Command("remove", "Remove a guest and delete their account") { idOption, memberIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [Owned, "Deletes their availability, player notes and shares with them; pages",
            "they wrote stay, without an author."]);
        command.AddExamples("grimoire-cli campaigns guests remove --id <campaign-id> --member-id <member-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignGuestsService(client).RemoveAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(memberIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateShareCommand()
    {
        var idOption = CampaignIdOption();
        var memberIdOption = MemberIdOption();
        var command = new Command("share", "Invite text and link carrying a guest's code") { idOption, memberIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [Owned, Code, "The link is built from the server's BASE_URL."]);
        command.AddExamples("grimoire-cli campaigns guests share --id <campaign-id> --member-id <member-id>");
        command.AddResponseExample<Generated.Models.GuestShareTemplateOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignGuestsService(client).ShareAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(memberIdOption)!));
            return 0;
        });
        return command;
    }
}
