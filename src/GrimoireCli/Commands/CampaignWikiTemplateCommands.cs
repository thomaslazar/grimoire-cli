using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignWikiTemplateCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    private static readonly string[] OwnerOnly = ["Campaign owner only; refused while the campaign is archived."];

    public static Command Create()
    {
        var command = new Command("templates", "A campaign's note templates for new wiki pages");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateImportCommand());
        command.Subcommands.Add(CreateExportCommand());
        command.Subcommands.Add(CreateUseCommand());
        return command;
    }

    private static Option<string> CampaignIdOption() =>
        new("--id") { Description = "Campaign ID", Required = true };

    private static Option<string> TemplateIdOption() =>
        new("--template-id") { Description = "Note template ID", Required = true };

    private static Command CreateListCommand()
    {
        var idOption = CampaignIdOption();
        var command = new Command("list", "List the campaign's note templates") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, OwnerOnly);
        command.AddExamples("grimoire-cli campaigns wiki templates list --id <campaign-id>");
        command.AddResponseExample<Generated.Models.WikiTemplateListOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiTemplateService(client).ListAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = CampaignIdOption();
        var templateIdOption = TemplateIdOption();
        var command = new Command("get", "Get one note template with its body") { idOption, templateIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. OwnerOnly, "",
            "body comes back without its frontmatter; those fields are in defaults."]);
        command.AddExamples("grimoire-cli campaigns wiki templates get --id <campaign-id> --template-id <template-id>");
        command.AddResponseExample<Generated.Models.WikiTemplateDetailOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiTemplateService(client).GetAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(templateIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateCreateCommand()
    {
        var idOption = CampaignIdOption();
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("create", "Write a new note template") { idOption, inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. OwnerOnly, "",
            "defaults are the starting fields of a page made by use. Omitted, any",
            "frontmatter already in body is kept. An unknown visibility or page_type",
            "is stored as gm or note.",
            "",
            "body is a wiki page body; its syntax is in campaigns wiki create --help."]);
        command.AddExamples(
            "echo '{\"name\":\"NPC\",\"category\":\"Characters\",\"body\":\"## Appearance\\n\\n## Motives\",\"defaults\":{\"title\":\"New NPC\",\"visibility\":\"gm\"}}' | grimoire-cli campaigns wiki templates create --id <campaign-id> --stdin");
        command.AddRequestShape<Generated.Models.WikiTemplateInput>();
        command.AddResponseExample<Generated.Models.WikiTemplateDetailOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.WikiTemplateInput.CreateFromDiscriminatorValue, "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiTemplateService(client).CreateAsync(parseResult.GetValue(idOption)!, body));
            return 0;
        });
        return command;
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = CampaignIdOption();
        var templateIdOption = TemplateIdOption();
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("update", "Edit a note template") { idOption, templateIdOption, inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. OwnerOnly, "",
            "Send only the fields to change. A sent defaults replaces all five",
            "fields: any left out reset (visibility gm, page_type note, the rest",
            "empty). Sending only body keeps the current defaults. An unknown",
            "visibility or page_type is stored as gm or note."]);
        command.AddExamples(
            "echo '{\"description\":\"Stat-free NPC sheet\"}' | grimoire-cli campaigns wiki templates update --id <campaign-id> --template-id <template-id> --stdin");
        command.AddRequestShape<Generated.Models.WikiTemplateUpdate>();
        command.AddResponseExample<Generated.Models.WikiTemplateDetailOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.WikiTemplateUpdate.CreateFromDiscriminatorValue, "pass it with --template-id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiTemplateService(client).UpdateAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(templateIdOption)!, body));
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = CampaignIdOption();
        var templateIdOption = TemplateIdOption();
        var command = new Command("delete", "Delete a note template") { idOption, templateIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, OwnerOnly);
        command.AddExamples("grimoire-cli campaigns wiki templates delete --id <campaign-id> --template-id <template-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiTemplateService(client).DeleteAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(templateIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateImportCommand()
    {
        var idOption = CampaignIdOption();
        var fileOption = new Option<string>("--file") { Description = "Local file to import", Required = true };
        var command = new Command("import", "Add a note template from a .md file or template .zip") { idOption, fileOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. OwnerOnly, "",
            "Reads .md/.markdown/.txt, or a .zip holding one .md and an optional .yml",
            "manifest — what export writes. The name comes from the manifest, else",
            "the frontmatter title, else the first # heading, else the file name.",
            "Max 512 KB."]);
        command.AddExamples("grimoire-cli campaigns wiki templates import --id <campaign-id> --file npc.zip");
        command.AddResponseExample<Generated.Models.WikiTemplateDetailOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            string result;
            try
            {
                result = await new CampaignWikiTemplateService(client).ImportAsync(
                    parseResult.GetValue(idOption)!, parseResult.GetValue(fileOption)!);
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateExportCommand()
    {
        var idOption = CampaignIdOption();
        var templateIdOption = TemplateIdOption();
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for the bytes to stdout",
            Required = true,
        };
        var command = new Command("export", "Export a note template as a .zip") { idOption, templateIdOption, outputOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. OwnerOnly, "",
            "The zip holds <slug>/<slug>.yml and <slug>/<slug>.md.",
            "",
            "--output - writes the zip to stdout; a path writes the file and prints",
            "{path, bytes}."]);
        command.AddExamples("grimoire-cli campaigns wiki templates export --id <campaign-id> --template-id <template-id> --output npc.zip");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            await using var stream = await new CampaignWikiTemplateService(client).ExportAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(templateIdOption)!);
            try
            {
                await ConsoleOutput.WriteStreamAsync(stream, parseResult.GetValue(outputOption)!);
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            return 0;
        });
        return command;
    }

    private static Command CreateUseCommand()
    {
        var idOption = CampaignIdOption();
        var templateIdOption = TemplateIdOption();
        var command = new Command("use", "Create a wiki page from a note template") { idOption, templateIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. OwnerOnly, "",
            "Every use creates a new page; nothing is overwritten. Title: defaults",
            "title, else the body's first # heading, else the template name; a",
            "repeated title gets a suffixed slug. session_date is never set, even",
            "for a session page. Unresolved [[links]] get stubs, as on campaigns wiki",
            "create. Rename the page with campaigns wiki update."]);
        command.AddExamples("grimoire-cli campaigns wiki templates use --id <campaign-id> --template-id <template-id>");
        command.AddResponseExample<Generated.Models.WikiTemplateUseOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiTemplateService(client).UseAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(templateIdOption)!));
            return 0;
        });
        return command;
    }
}
