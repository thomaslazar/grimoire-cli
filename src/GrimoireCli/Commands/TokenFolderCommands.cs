using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

/// <summary>
/// Folder tags under `tokens`. A second tagging layer addressed by path, keyed by
/// the path in the body rather than by a parent id. There is no delete: the
/// collection offers none.
/// </summary>
public static class TokenFolderCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("folders", "Token folders and their tags");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateSetCommand());
        command.Subcommands.Add(CreateBatchSetCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List tagged token folders");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Reports what has been tagged, never what is on disk — a row exists",
            "only once a path has been given tags.",
            "",
            "Tags come back in display casing here; set and batch-set echo the",
            "stored internal keys instead.");
        command.AddExamples("grimoire-cli tokens folders list");
        command.AddResponseExample<Generated.Models.TokenFoldersResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new TokensService(client);
            var result = await service.FoldersListAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateSetCommand()
    {
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("set", "Replace one token folder's tags")
        {
            inputOption, stdinOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "tags replace the folder's set; an empty list clears it and keeps the",
            "row. The folder is addressed by path in the body: a path that is not",
            "on disk still creates a row, and no endpoint removes one — a typo is",
            "permanent.",
            "",
            "A tag reaches every token at or below the path in tags items and",
            "search. tokens get matches folder_path exactly, so folder_tags on a",
            "token in a subfolder of the tagged path reads empty.");
        command.AddExamples(
            "echo '{\"path\":\"Monsters\",\"tags\":[\"goblin\"]}' | grimoire-cli tokens folders set --stdin");
        command.AddRequestShape<Generated.Models.FolderTagsUpdate>();
        command.AddResponseExample<Generated.Models.Backend__routers__tokens___schemas__FolderTagsOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.FolderTagsUpdate.CreateFromDiscriminatorValue,
                    "pass it as path");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            var service = new TokensService(client);
            var result = await service.FoldersSetAsync(body);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateBatchSetCommand()
    {
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("batch-set", "Set tags on many token folders in one transaction")
        {
            inputOption, stdinOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "1 to 1000 folders. Each replaces that folder's tags, as set does, and",
            "each creates a permanent row the same way.",
            "",
            "All or nothing: there is no per-item error list and no exit 3 here.");
        command.AddExamples("grimoire-cli tokens folders batch-set --input folders.json");
        command.AddRequestShape<Generated.Models.BulkFolderTags>();
        command.AddResponseExample<Generated.Models.TokenFoldersResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.BulkFolderTags.CreateFromDiscriminatorValue,
                    "pass each path inside folders");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            var service = new TokensService(client);
            var result = await service.FoldersBatchSetAsync(body);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
