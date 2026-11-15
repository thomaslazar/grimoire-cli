using System.CommandLine;

namespace GrimoireCli.Commands;

/// <summary>
/// The category values a book carries, for every command that filters by
/// --category: CATEGORY_MAP and WARGAME_CATEGORY_MAP in
/// indexer/constants.py at v1.11.0.
/// </summary>
internal static class BookCategoryHelp
{
    public static void AddTo(Command command) =>
        command.AddHelpSection("Categories", HelpSectionPosition.Top,
            "Role-playing (books/): core, supplement, adventure, character-sheet, map,",
            "  handout, homebrew, starter-set, magazine",
            "Wargame (armory/): core, army-book, supplement, campaign, mission-pack,",
            "  unit-reference, errata, starter-set, painting-guide, lore, magazine",
            "",
            "--category takes this normalised slug, not the folder name ('supplement',",
            "not 'Supplements'), and is case-sensitive. A Campaigns folder is",
            "adventure under books/ but campaign under armory/. An unmapped folder",
            "becomes its own slug. A book with no category folder is core, or",
            "uncategorized under a system-agnostic container.");
}
