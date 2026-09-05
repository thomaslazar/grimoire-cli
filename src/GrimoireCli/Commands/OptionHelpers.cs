using System.CommandLine;

namespace GrimoireCli.Commands;

/// <summary>Option factories shared across command groups.</summary>
public static class OptionHelpers
{
    /// <summary>
    /// A string option restricted to a fixed value set, rejected at parse time and
    /// offered as shell completions. A server that silently falls back to a default
    /// when given an unknown value would otherwise return differently-shaped data
    /// with exit 0, so an unrecognised value is rejected here instead. The rendered
    /// help lists the values itself, so the description must not repeat them.
    /// </summary>
    public static Option<string?> Choice(string name, string description, string[] allowed)
    {
        var option = new Option<string?>(name) { Description = description };
        option.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (value is not null && !allowed.Contains(value))
                result.AddError($"'{value}' is not a valid value for {name}. Must be one of: {string.Join(", ", allowed)}");
        });
        option.CompletionSources.Add(allowed);
        return option;
    }

    /// <summary>
    /// An integer option constrained to a range, rejected at parse time. The
    /// backup settings fields it guards are clamped by the server rather than
    /// refused — `routers/backups/core.py` stores `max(0, min(23, hour))` and
    /// answers 200 — so an out-of-range value would otherwise be silently
    /// stored as a different value. <paramref name="max"/> is null for a field
    /// with a floor and no ceiling.
    /// </summary>
    public static Option<int?> Range(string name, string description, int min, int? max = null)
    {
        var option = new Option<int?>(name) { Description = description };
        option.Validators.Add(result =>
        {
            // Reading the token rather than the converted value: an unconvertible
            // token ("abc", "", "3.5", one that overflows int) makes
            // GetValueOrDefault throw out of Parse, and the framework already
            // reports it as a parse error of its own.
            if (result.Tokens.Count == 0 || !int.TryParse(result.Tokens[0].Value, out var value)) return;
            if (value < min || (max is not null && value > max))
                result.AddError(max is null
                    ? $"'{value}' is not a valid value for {name}. Must be {min} or greater."
                    : $"'{value}' is not a valid value for {name}. Must be between {min} and {max}.");
        });
        return option;
    }
}
