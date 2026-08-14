using GrimoireCli.Models;

namespace GrimoireCli.Commands;

/// <summary>
/// Maps a scan-trigger response to an exit code. 3 means the request succeeded
/// (HTTP 200) and reported already_running — a scan was in flight, so the
/// requested one never started. stdout still carries the status, so a caller
/// that polls scan-status can tell it is watching someone else's scan.
/// </summary>
public static class ScanExit
{
    public static int CodeFor(ScanTriggerResult result) => result.Status == "already_running" ? 3 : 0;
}
