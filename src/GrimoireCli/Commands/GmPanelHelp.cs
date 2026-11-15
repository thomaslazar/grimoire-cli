using System.CommandLine;

namespace GrimoireCli.Commands;

/// <summary>The panel kinds and their config and state keys (services/gm_screen/panels.py at v1.11.0).</summary>
internal static class GmPanelHelp
{
    public static void AddTo(Command command)
    {
        command.AddHelpSection("Panel kinds", HelpSectionPosition.Top,
            "The grid is 12 columns. Unknown keys and bad values in config and state",
            "silently become defaults. resets is scene, session, rest or \"\" (never).",
            "A row's id is kept when given, so a later update can address it.",
            "",
            "note       config page_id or page_slug: a page in this campaign (an unknown",
            "           page_id is a 404)",
            "party      config stats [{value, label, max, style number|pips}],",
            "           include_inactive; value names a field from party's fields, max",
            "           one too or a number",
            "counter    config min, max, start, style number|pips, resets; state value",
            "clocks     config mode countdown|clock; state items [{id, name, size,",
            "           ticks, note, resets, loops}]",
            "pools      config resets; state items [{id, name, tokens, note}]",
            "scratch    state text",
            "initiative config mode ordered|sides, sides [{key, label}], resets;",
            "           state round, turn, side, first, entries [{id, name, score, side,",
            "           acted, note, character_id}]",
            "reference  config sections [{id, title, text}], text in markdown",
            "table      config mode list|join, columns [{id, label, entries}];",
            "           state rolls, one 1-based entry number per column");
    }
}
