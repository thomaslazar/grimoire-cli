using System.CommandLine;
using GrimoireCli.Api;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class LibraryCommand
{
    private static readonly string[] MetadataModes = ["new", "missing", "replace"];

    public static Command Create()
    {
        var command = new Command("library", "Scan and index the library");
        command.Subcommands.Add(CreateRescanCommand());
        command.Subcommands.Add(CreateScanStatusCommand());
        command.Subcommands.Add(CreateCancelScanCommand());
        command.Subcommands.Add(CreateCleanupMissingCommand());
        return command;
    }

    private static Command CreateRescanCommand()
    {
        var scopeOption = new Option<string?>("--scope") { Description = "Restrict the scan to a subtree" };
        var metadataModeOption = OptionHelpers.Choice("--metadata-mode", "Re-apply OPF sidecar metadata", MetadataModes);
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("rescan", "Scan the library for new and changed files")
        {
            scopeOption, metadataModeOption, serverOption, tokenOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The only command that finds a file copied into the library by hand;",
            "books rescan re-reads a book the server already knows.",
            "",
            "--scope is a path from the library root beginning books/, maps/,",
            "tokens/ or audio/ — the directory part of a book's relative_path in",
            "systems get, not the file path itself.",
            "A scope matching nothing still reports scan_started.",
            "",
            "Exit 3 is HTTP 200 with already_running: a scan was already in flight",
            "and this one did not start — a books rescan still running is one",
            "cause.");
        command.AddExamples("grimoire-cli library rescan --scope \"books/Shadowrun/4 DE\"");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new LibraryService(client);
            var result = await service.RescanAsync(
                parseResult.GetValue(scopeOption), parseResult.GetValue(metadataModeOption));
            ConsoleOutput.WriteRawJson(result);
            return ScanExit.CodeFor(GrimoireApiClient.ReadStringProperty(result, "status"));
        });
        return command;
    }

    private static Command CreateScanStatusCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("scan-status", "Show the running scan's progress")
        {
            serverOption, tokenOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "phase is scanning, indexing or ocr; the counters belong to the scan in",
            "flight.",
            "",
            "A loose file directly under books/ counts toward total_books but is",
            "never scanned, so scanned_books >= total_books never becomes true. Poll",
            "running instead.");
        command.AddResponseExample<ScanStatus>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new LibraryService(client);
            var result = await service.ScanStatusAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateCancelScanCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("cancel-scan", "Stop the running scan")
        {
            serverOption, tokenOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Requests a graceful stop; the scan ends at its next checkpoint. Exits 0",
            "whether or not one was running.");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new LibraryService(client);
            var response = await service.CancelScanAsync();
            ConsoleOutput.WriteRawJson(response);
            return 0;
        });
        return command;
    }

    private static Command CreateCleanupMissingCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("cleanup-missing", "Remove DB entries for files no longer on disk")
        {
            serverOption, tokenOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Deletes DB rows for files no longer on disk, each book's search index",
            "and bookmarks with it, then prunes systems whose books are all gone —",
            "unless a campaign or a surviving child keeps one. Never touches files.",
            "",
            "Normally a no-op. Run it after restructuring the library on disk.",
            "",
            "A library directory that is absent rather than hung reads as wholly",
            "deleted, and a rescan does not restore hand-entered metadata or",
            "bookmarks. A hung mount is safe — the server treats a timed-out path",
            "as present.",
            "",
            "409 while a scan is running; commits per row, so a failure part-way",
            "leaves earlier removals applied.");
        command.AddExamples("grimoire-cli library cleanup-missing");
        command.AddResponseExample<CleanupResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new LibraryService(client);
            var result = await service.CleanupMissingAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
