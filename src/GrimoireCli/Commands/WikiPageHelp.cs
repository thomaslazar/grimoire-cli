using System.CommandLine;

namespace GrimoireCli.Commands;

/// <summary>
/// The page-body and visibility rules campaigns wiki create and update share:
/// the link and embed grammar, GM secrets, the stubs a save creates, and who can
/// see and edit each visibility level (routers/campaigns/wiki.py,
/// wikilinks.py, _wiki_helpers.py at v1.8.0).
/// </summary>
internal static class WikiPageHelp
{
    public static void AddTo(Command command)
    {
        command.AddHelpSection("Page body", HelpSectionPosition.Top,
            "body is markdown. [[Title]] links a page; [[Title|label]] shows label;",
            "[[Title:#Heading]] jumps to a heading; [[Title:id-<page-id>]] pins the",
            "page so the link survives renames (titles from campaigns wiki titles).",
            "Saving creates an empty stub for each unknown unpinned [[Title]], with",
            "this page's visibility; a pinned link to a missing page creates none.",
            "Renaming a page rewrites links to its old title.",
            "",
            "Embeds are not links: [[book:<id>]], [[book:<id>:<page>]], [[map:<id>]],",
            "[[token:<id>]], [[audio:<id>]], [[model:<id>]] take library ids;",
            "[[image:<file-id>]] and [[file:<file-id>]] take campaign file ids. Any",
            "other prefix is a page title. The web editor offers only items linked",
            "to the campaign.",
            "",
            "||text|| is a GM secret: only the campaign owner sees it. Anyone else's",
            "||…|| is stored as literal text.");
        command.AddHelpSection("Visibility", HelpSectionPosition.Top,
            "gm — the author alone (the GM's private pages, or a player's self-only",
            "note); group — every member, and every member can edit; members — the",
            "author plus shared_user_ids (read) and shared_write_user_ids (read and",
            "write). Only the author changes visibility or sharing. A personal",
            "campaign stores every page as gm.");
    }
}
